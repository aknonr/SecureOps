using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using SecureOps.Shared.Contracts.ServiceAccounts;
using SecureOps.Ui.Services;
using SecureOps.Ui.Services.ServiceAccounts;

namespace SecureOps.Ui.Pages.ServiceAccounts;

/// <summary>
/// Import wizard. The file stays in this circuit only to allow a re-stage with an edited generic mapping; the server
/// keeps its own copy of the staged bytes, and the commit carries one idempotency key per reviewed preview.
/// </summary>
public partial class ServiceAccountImports
{
    private const long _maxBytes = 20L * 1024 * 1024;
    private static readonly (int Number, string Title)[] _steps = [(1, "Dosya ve kaynak bilgisi"), (2, "Önizleme ve kararlar"), (3, "Onay ve sonuç")];
    private static readonly (string Value, string Label)[] _profiles =
    [
        (ServiceAccountImportProfiles.CoordinationList, "Koordinasyon listesi (hesap ve kaynak gözlemi)"),
        (ServiceAccountImportProfiles.DbaHandover, "DBA devir listesi"),
        (ServiceAccountImportProfiles.LegacyPackage, "Eski çalışma kitabı veri paketi (.json)"),
        (ServiceAccountImportProfiles.LegacyWorkbook, "Eski çalışma kitabı (.xlsx)"),
        (ServiceAccountImportProfiles.Generic, "Genel tablo (sütun eşlemeli)")
    ];
    private static readonly string[] _provenances =
    [
        "Dosya adındaki tarih", "Dosyayı ileten e-postanın tarihi", "Dosya sahibinin yazılı beyanı", "Dosya içindeki rapor tarihi", "Tarih bilinmiyor"
    ];

    private static readonly (string Value, string Label)[] _coverages =
    [
        (ServiceAccountImportCoverage.Unknown, "Bilinmiyor (listede olmayan hesaplar için çıkarım yapılmaz)"),
        (ServiceAccountImportCoverage.Partial, "Kısmi liste (listede olmayan hesaplar için çıkarım yapılmaz)"),
        (ServiceAccountImportCoverage.Complete, "Seçilen kurumların tam listesi")
    ];

    private ImportBatchView? _batch;
    private IReadOnlyList<OrganizationView> _organizations = [];
    private ImportRowPage? _rows;
    private IReadOnlyList<ImportHistoryItem> _history = [];
    private (string Name, byte[] Content)? _file;
    private string? _fileError;
    private string? _commitKey;
    private (string? Classification, string? Kind, bool DecisionsOnly, int Page) _rowQuery = (null, null, false, 1);
    private readonly StageForm _form = new();

    /// <inheritdoc />
    protected override string RequiredCapability => ServiceAccountCapabilities.Import;

    private int Step => _batch is null ? 1 : _batch.Status == "Committed" ? 3 : 2;

    /// <inheritdoc />
    protected override Task LoadAsync() => RunSerializedAsync(async token =>
    {
        _history = await Api.GetAsync<IReadOnlyList<ImportHistoryItem>>("/imports", token);
        _organizations = await Api.GetAsync<IReadOnlyList<OrganizationView>>("/organizations", token);
    });

    private bool CoverageSupported => _form.Profile is ServiceAccountImportProfiles.CoordinationList or ServiceAccountImportProfiles.LegacyPackage
        or ServiceAccountImportProfiles.LegacyWorkbook;

    private bool CoverageReady => !CoverageSupported || _form.Coverage != ServiceAccountImportCoverage.Complete
        || _form.CoverageOrganizations.Any() && _form.ReportDate is not null;

    private async Task ChooseAsync(InputFileChangeEventArgs args)
    {
        (_file, _fileError) = (null, null);
        IBrowserFile file = args.File;
        if (file.Size is 0 or > _maxBytes)
        {
            _fileError = "Dosya boş veya 20 MB sınırını aşıyor.";
            return;
        }

        using MemoryStream buffer = new();
        await using Stream stream = file.OpenReadStream(_maxBytes);
        await stream.CopyToAsync(buffer);
        _file = (file.Name, buffer.ToArray());
    }

    private Task StageAsync() => StageWithAsync(null);

    private Task RestageAsync(IReadOnlyList<ImportColumnMapping> mapping) => StageWithAsync(mapping);

    private Task StageWithAsync(IReadOnlyList<ImportColumnMapping>? mapping) => RunAsync(async token =>
    {
        (string name, byte[] content) = _file!.Value;
        _batch = await Api.UploadAsync<ImportBatchView>("/imports", name, ContentType(name), content, new Dictionary<string, string?>
        {
            ["profile"] = _form.Profile,
            ["sourceReportDate"] = ServiceAccountUiText.ToDate(_form.ReportDate)?.ToString("yyyy-MM-dd"),
            ["sourceDateProvenance"] = _form.Provenance?.Trim(),
            ["declaredScope"] = Blank(_form.DeclaredScope),
            ["declaredDomain"] = Blank(_form.DeclaredDomain),
            ["sheet"] = Blank(_form.Sheet),
            ["targetTeam"] = _form.Profile == ServiceAccountImportProfiles.DbaHandover ? Blank(_form.TargetTeam) : null,
            ["mappingJson"] = mapping is null ? null : JsonSerializer.Serialize(mapping, ApiResponseReader.JsonOptions),
            ["coverage"] = CoverageSupported ? _form.Coverage : ServiceAccountImportCoverage.Unknown,
            ["coverageOrganizationIds"] = CoverageSupported && _form.Coverage == ServiceAccountImportCoverage.Complete
                ? string.Join(',', _form.CoverageOrganizations) : null
        }, token);
        _commitKey = null;
        _rowQuery = (null, null, false, 1);
        await LoadRowsAsync(token);
    });

    private Task OpenAsync(Guid id) => RunAsync(async token =>
    {
        _batch = await Api.GetAsync<ImportBatchView>($"/imports/{id}", token);
        (_commitKey, _file, _rowQuery) = (null, null, (null, null, false, 1));
        await LoadRowsAsync(token);
    });

    private Task QueryRowsAsync((string? Classification, string? Kind, bool DecisionsOnly, int Page) query) => RunAsync(async token =>
    {
        _rowQuery = query;
        await LoadRowsAsync(token);
    });

    private async Task LoadRowsAsync(CancellationToken token)
    {
        if (_batch is null || _batch.Result is not null)
        {
            _rows = null;
            return;
        }

        List<string> query = [$"page={_rowQuery.Page}", "pageSize=50"];
        if (_rowQuery.Classification is { } c) { query.Add("classification=" + c); }
        if (_rowQuery.Kind is { } k) { query.Add("kind=" + k); }
        if (_rowQuery.DecisionsOnly) { query.Add("decisionsOnly=true"); }
        _rows = await Api.GetAsync<ImportRowPage>($"/imports/{_batch.Id}/rows?" + string.Join('&', query), token);
    }

    private async Task DecideAsync(ImportDecisionsRequest request)
    {
        bool saved = await RunAsync(async token =>
        {
            _batch = await Api.SendAsync<ImportBatchView>(HttpMethod.Put, $"/imports/{_batch!.Id}/decisions", request with { ExpectedDecisionVersion = _batch.DecisionVersion }, token);
            _commitKey = null;
            await LoadRowsAsync(token);
        });
        if (!saved && Problem is { Kind: UiProblemKind.Conflict } conflict)
        {
            await RunAsync(async token => { _batch = await Api.GetAsync<ImportBatchView>($"/imports/{_batch!.Id}", token); await LoadRowsAsync(token); });
            Problem = conflict;
        }
    }

    private Task RefreshAsync() => RunAsync(async token =>
    {
        _batch = await Api.SendAsync<ImportBatchView>(HttpMethod.Post, $"/imports/{_batch!.Id}/preview", new { }, token);
        _commitKey = null;
        await LoadRowsAsync(token);
    });

    /// <summary>One key per reviewed preview/decision version: a repeated click or retry returns the stored result.</summary>
    private Task CommitAsync() => RunAsync(async token =>
    {
        ImportBatchView batch = _batch!;
        _commitKey ??= $"sa-import-{batch.Id:N}-{batch.PreviewVersion}-{batch.DecisionVersion}-{Guid.NewGuid():N}"[..64];
        _batch = await Api.SendAsync<ImportBatchView>(HttpMethod.Post, $"/imports/{batch.Id}/commit", new ImportCommitRequest(batch.PreviewVersion, batch.DecisionVersion),
            token, new Dictionary<string, string> { ["Idempotency-Key"] = _commitKey });
        _rows = null;
    });

    private async Task ResetAsync()
    {
        (_batch, _rows, _file, _commitKey, Problem) = (null, null, null, null, null);
        await LoadAsync();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".csv" => "text/csv",
        ".json" => "application/json",
        _ => "application/octet-stream"
    };

    private static string Accept(string profile) => profile switch
    {
        ServiceAccountImportProfiles.LegacyPackage => ".json",
        ServiceAccountImportProfiles.LegacyWorkbook => ".xlsx",
        _ => ".xlsx,.csv"
    };

    private static string ProfileLabel(string profile) => _profiles.FirstOrDefault(p => p.Value == profile).Label ?? profile;

    private static string ProfileHelp(string profile) => profile switch
    {
        ServiceAccountImportProfiles.CoordinationList => "Hesap adı, domain, kurum, parola ve son oturum sütunları okunur. Yalnız \"tam liste\" beyan edilirse, seçilen kurumlarda olup bu listede olmayan hesaplar \"bu listede yok\" gözlemi alır; hiçbir hesap silinmez veya kapanmaz.",
        ServiceAccountImportProfiles.DbaHandover => "Kaynak ekip, kullanan ekip ve devir işareti okunur. Devir kabulü sayılmaz; hedef ekip belirtilirse devir bildirimi oluşur.",
        ServiceAccountImportProfiles.LegacyPackage => "Eski çalışma kitabının hazırlanmış veri paketi. Aynı çalışma kitabı .xlsx olarak da aktarılırsa satırlar tekrar oluşturulmaz.",
        ServiceAccountImportProfiles.LegacyWorkbook => "Yalnız giriş sayfaları aktarılır; arşiv sayfaları uyarı olarak listelenir. Formüller hesaplanmaz, kayıtlı değerleri okunur.",
        _ => "Sütunlar önizlemede alanlara eşlenir; eşlemeyi değiştirip yeniden önizleyebilirsiniz."
    };

    private static string CoverageLabel(string coverage) => coverage switch
    {
        ServiceAccountImportCoverage.Complete => "Tam liste",
        ServiceAccountImportCoverage.Partial => "Kısmi liste",
        _ => "Kapsam bilinmiyor"
    };

    private string CoverageOrganizationNames(IReadOnlyList<Guid>? ids) =>
        ids is null || ids.Count == 0 ? "kurum seçilmedi"
            : string.Join(", ", ids.Select(id => _organizations.FirstOrDefault(o => o.Id == id)?.Name ?? "erişim dışı kurum"));

    private static string BatchStatus(string status) => status switch
    {
        "Staged" => "Yüklendi",
        "Previewed" => "Önizleme hazır",
        "Committed" => "Aktarıldı",
        "Abandoned" => "Bırakıldı",
        _ => status
    };

    private sealed class StageForm
    {
        public string Profile { get; set; } = ServiceAccountImportProfiles.CoordinationList;
        public DateTime? ReportDate { get; set; }
        public string? Provenance { get; set; }
        public string? DeclaredScope { get; set; }
        public string? DeclaredDomain { get; set; }
        public string? Sheet { get; set; }
        public string? TargetTeam { get; set; }
        public string Coverage { get; set; } = ServiceAccountImportCoverage.Unknown;
        public IEnumerable<Guid> CoverageOrganizations { get; set; } = [];
    }
}
