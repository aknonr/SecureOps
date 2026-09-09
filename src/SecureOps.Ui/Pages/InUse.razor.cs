using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.InUse;
using SecureOps.Ui.Services;

namespace SecureOps.Ui.Pages;

/// <summary>API-authorized independent local In Use workspace.</summary>
public partial class InUse
{
    /// <summary>Optional persisted record identifier.</summary>
    [Parameter] public Guid? Id { get; set; }
    private AccessSnapshot? _access;
    private InUsePage? _page;
    private InUseRecord? _record;
    private InUseReport? _report;
    private IReadOnlyList<InUseAssignee> _assignees = [];
    private UiProblem? _problem;
    private string? _notice;
    private string _search = "", _view = "all", _status = "", _assignee = "", _reason = "", _notes = "";
    private string _editingServer = "";
    private string? _validation;
    private (AnswerEdit Target, string Before, string After)[]? _changes;
    private readonly ElementReference[] _answerElements = new ElementReference[3];
    private int? _focusAnswer;
    private bool _busy, _dirty, _reloadRequested;
    private int _number = 1, _size = 25, _sheet;
    private List<AnswerEdit> _answers = [];
    private readonly HashSet<string> _selected = [];
    private readonly CancellationTokenSource _lifetime = new();
    private bool Can(string capability) => _access?.Can(capability) == true;
    private static string Relationship(string state, int? count) => state switch
    {
        "Complete" => count == 0 ? "İlişkili kayıt yok (doğrulandı)" : $"{count} doğrulanmış kayıt",
        "Observed" => $"{count} gözlenen kayıt; tamlık doğrulanmadı",
        "Partial" => $"Eksik sonuç; {count} kayıt korunuyor, güncel üyelik doğrulanmalı",
        "Forbidden" => "Kaynak ilişki erişimi yetersiz; önceki kanıt varsa korunur",
        "Failed" => "İlişki okuması başarısız; önceki kanıt varsa korunur",
        "Ambiguous" => "Eşleme belirsiz; önceki kanıt varsa korunur",
        _ => "Henüz sorgulanmadı / sözleşme bekleniyor"
    };
    private bool CanEdit => Can(Capabilities.InUseReview) && _record?.AssigneeId == _access?.Access?.UserId;
    private string NextAction => _record?.AssigneeId is null ? "Yetkili koordinatör bir inceleyici atasın."
        : !CanEdit ? "Atanan inceleyicinin doğrulaması bekleniyor."
        : _record.Status == "Stale" ? "Değişen kaynak kanıtına göre cevapları yeniden doğrulayın."
        : _record.Draft is null || _dirty ? "Sunucu cevaplarını inceleyip yerel taslağı kaydedin." : "Kaydedilen taslağın Excel önizlemesini kontrol edin.";

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    { AccessProvider.Changed += AccessChanged; await InitializeAsync(); }
    /// <inheritdoc />
    protected override async Task OnParametersSetAsync() { if (_access is not null) { await LoadAsync(); } }
    private async Task InitializeAsync()
    { _access = await AccessProvider.GetAsync(_lifetime.Token); if (Can(Capabilities.InUseView)) { await LoadAsync(); } }
    private Task FilterAsync() { _number = 1; return LoadAsync(); }
    private Task SearchAsync(string value) { _search = value; return FilterAsync(); }
    private Task PreviousAsync() { _number = Math.Max(1, _number - 1); return LoadAsync(); }
    private Task NextAsync() { _number++; return LoadAsync(); }
    private Task LoadAsync()
    {
        if (_busy)
        { _reloadRequested = true; return Task.CompletedTask; }
        return ExecuteAsync(async () =>
        {
            _report = null;
            _record = null;
            if (Id is Guid id)
            {
                SetRecord(await Api.GetAsync<InUseRecord>($"/{id}", _lifetime.Token));
                if (Can(Capabilities.InUseAssign))
                { _assignees = await Api.GetAsync<InUseAssignee[]>("/assignees", _lifetime.Token); }
            }
            else
            {
                _page = await Api.GetAsync<InUsePage>($"?search={Uri.EscapeDataString(_search)}&view={_view}&page={_number}&pageSize={_size}"
                    + (string.IsNullOrEmpty(_status) ? "" : "&status=" + _status), _lifetime.Token);
            }
        });
    }
    private Task RefreshAsync() => ExecuteAsync(async () =>
    {
        InUseRefreshState state = await Api.SendAsync<InUseRefreshState>(HttpMethod.Post, "/refresh", new RefreshInUseRequest(Guid.NewGuid()), _lifetime.Token);
        _notice = state.Issue is null ? "Sınırlı kaynak okuması tamamlandı." : RefreshIssue(state.Issue);
        _page = await Api.GetAsync<InUsePage>($"?page={_number}&pageSize={_size}&view={_view}&search={Uri.EscapeDataString(_search)}"
            + (string.IsNullOrEmpty(_status) ? "" : "&status=" + _status), _lifetime.Token);
    });
    private Task AssignAsync() => ExecuteAsync(async () =>
    {
        if (_record is null)
        { return; }
        SetRecord(await Api.SendAsync<InUseRecord>(HttpMethod.Put, $"/{_record.Id}/assignment",
            new AssignInUseRequest(_record.Version, Guid.TryParse(_assignee, out Guid userId) ? userId : null, _reason), _lifetime.Token));
        _reason = "";
        _notice = "Yerel atama kaydedildi.";
    });
    private Task SaveAsync() => ExecuteAsync(async () =>
    {
        if (_record is null)
        { return; }
        SetRecord(await Api.SendAsync<InUseRecord>(HttpMethod.Put, $"/{_record.Id}/draft",
            new SaveInUseDraftRequest(_record.Version, _record.SourceVersion,
                _answers.Select(a => new InUseAnswer(a.ServerId, a.Check, a.Value, a.Evidence)).ToArray(), _notes), _lifetime.Token));
        _notice = "Yerel inceleme taslağı kaydedildi.";
    });
    private Task PreviewAsync() => ExecuteAsync(async () => { _report = await ReportAsync(); _sheet = 0; });
    private Task DownloadAsync(long? archivedVersion = null) => ExecuteAsync(async () =>
    {
        if (archivedVersion is null && !Ready())
        { return; }
        _report = await Api.SendAsync<InUseReport>(HttpMethod.Post, $"/{_record!.Id}/report",
            new ExportInUseRequest(_record.Version, Archive: archivedVersion is null, ArchivedVersion: archivedVersion), _lifetime.Token);
        _record = _record with { ArchivedVersions = _record.ArchivedVersions.Append(_report.Version).Distinct().OrderDescending().ToArray() };
        await Js.InvokeVoidAsync("secureOpsDownload", _report.FileName,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Convert.ToBase64String(_report.Content));
    });
    private bool Ready()
    {
        InUseAnswer? missing = InUseChecks.Missing(_record!.Source, _answers.Select(a => new InUseAnswer(a.ServerId, a.Check, a.Value, "")).ToArray());
        if (missing is not null)
        {
            _editingServer = missing.ServerId;
            _validation = $"{missing.ServerId}: {Check(missing.Check)} için Evet veya Hayır seçin. Bilinmiyor cevabı yalnızca taslak olarak kaydedilebilir.";
            _focusAnswer = InUseChecks.OperatorCodes.ToList().IndexOf(missing.Check);
            return false;
        }
        if (!InUseChecks.RelationshipReady(_record.Source))
        { _validation = "Rapor hazırlığı için servis öğesi ilişkisi doğrulanmış ve en az bir sunucu bulunmuş olmalıdır."; return false; }
        _validation = null;
        return true;
    }
    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    { if (_focusAnswer is int index) { _focusAnswer = null; await _answerElements[index].FocusAsync(); } }
    private Task<InUseReport> ReportAsync() => Api.SendAsync<InUseReport>(HttpMethod.Post, $"/{_record!.Id}/report",
        new ExportInUseRequest(_record.Version), _lifetime.Token);
    private void SetRecord(InUseRecord record)
    {
        _record = record;
        _assignee = record.AssigneeId?.ToString("D") ?? "";
        _notes = record.Draft?.Notes ?? "";
        _dirty = false;
        _report = null;
        _selected.Clear();
        _changes = null;
        _validation = null;
        _editingServer = record.Source.Servers.FirstOrDefault()?.Id ?? "";
        _answers = record.Source.Servers.SelectMany(server => InUseChecks.OperatorCodes.Select(check =>
        {
            InUseAnswer? saved = record.Draft?.Answers.FirstOrDefault(a => a.ServerId == server.Id && a.Check == check);
            return new AnswerEdit(server.Id, check) { Value = saved?.Value ?? "Unknown", Evidence = saved?.Evidence ?? "" };
        })).ToList();
    }
    private void SelectServer(string id, ChangeEventArgs args)
    { if (args.Value is true) { _selected.Add(id); } else { _selected.Remove(id); } _changes = null; }
    private void PreviewBulk() => _changes = _answers.Where(a => _selected.Contains(a.ServerId))
        .Select(a => (Target: a, Before: a.Value, After: _answers.Single(s => s.ServerId == _editingServer && s.Check == a.Check).Value))
        .Where(c => c.Before != c.After).ToArray();
    private void ApplyBulk()
    {
        if (!CanEdit || _changes is null)
        { return; }
        foreach ((AnswerEdit Target, string Before, string After) change in _changes)
        { change.Target.Value = change.After; }
        Dirty();
        _notice = "Toplu cevaplar taslakta. Kaydetmeden önce sunucu bazında inceleyin.";
    }
    private void Dirty() { _dirty = true; _report = null; _changes = null; _validation = null; }
    private async Task ExecuteAsync(Func<Task> operation)
    {
        if (_busy)
        { return; }
        _busy = true;
        _problem = null;
        _notice = null;
        try
        { await operation(); }
        catch (SecureOpsApiException ex) { _problem = ex.Problem; _report = null; }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { _busy = false; }
        if (_reloadRequested && !_lifetime.IsCancellationRequested)
        { _reloadRequested = false; await LoadAsync(); }
    }
    private void AccessChanged() => InvokeAsync(async () =>
    {
        _record = null;
        _page = null;
        _report = null;
        _answers.Clear();
        _access = await AccessProvider.GetAsync(_lifetime.Token);
        StateHasChanged();
    });
    /// <inheritdoc />
    public void Dispose() { AccessProvider.Changed -= AccessChanged; _lifetime.Cancel(); _lifetime.Dispose(); }
    private static string Evidence(InUseEvidence evidence) => (evidence.Value ?? "Bilinmiyor") + " · " + evidence.Source;
    private static string Field(InUseServer server, string field) => server.Fields.GetValueOrDefault(field)?.Value ?? "Bilinmiyor";
    private static string Time(DateTimeOffset? value) => value?.ToLocalTime().ToString("g") ?? "Henüz yok";
    private static string Status(string value) => value switch { "Draft" => "Yerel taslak", "Stale" => "Yeniden inceleme gerekli", _ => "İncelenmedi" };
    private static string Answer(string value) => value switch { "Yes" => "Evet", "No" => "Hayır", "NotApplicable" => "Uygulanamaz", _ => "Bilinmiyor / doğrulanmadı" };
    private static string Check(string value) => value switch
    {
        "InternetOut" => "Sunucudan internete erişim",
        "InternetIn" => "İnternetten sunucuya erişim",
        "Microsegmented" => "Mikrosegmentasyon",
        "NmsRequested" => "NMS dahil edilme talebi",
        "MemoryAlarm" => "Bellek alarmı",
        "CpuAlarm" => "CPU alarmı",
        "UpDownAlarm" => "Erişilebilirlik alarmı",
        "DiskAlarm" => "Disk alarmı",
        _ => "Teknik kontrol doğrulandı"
    };
    private static string RefreshIssue(string? issue) => issue switch
    {
        "NotRefreshed" => "Henüz kaynak okunmadı.",
        "SourceCompletenessUnverified" => "Kaynak listesinin ve ilişkilerin tamlığı doğrulanmadı.",
        "SourceTimeout" => "Kaynak okuması zaman aşımına uğradı. Yetkili operatör yeniden deneyebilir.",
        "SourceUnavailableOrMalformed" => "Kaynak okunamadı veya yanıt geçersiz. Entegrasyon sağlığını kontrol edin.",
        _ => "Kaynak kanıtı güncel olmayabilir; son başarılı okuma tarihini kontrol edin."
    };
    private sealed class AnswerEdit(string serverId, string check)
    {
        public string ServerId { get; } = serverId;
        public string Check { get; } = check;
        public string Value { get; set; } = "Unknown";
        public string Evidence { get; set; } = "";
    }
}
