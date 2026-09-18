using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SecureOps.Shared.Contracts.Announcements;
using SecureOps.Ui.Services;

namespace SecureOps.Ui.Shared.Components;

/// <summary>Explicit source review; polling has no authority to save, apply or replace editor content.</summary>
public partial class AnnouncementSourceReview
{
    /// <summary>Persisted owned draft identity.</summary>
    [Parameter] public Guid DraftId { get; set; }
    /// <summary>Editor revision, never substituted into an existing review.</summary>
    [Parameter] public long Version { get; set; }
    /// <summary>Dirty, busy or conflicted editor prevents mutations.</summary>
    [Parameter] public bool Locked { get; set; }
    /// <summary>Explicit retrieval first persists current operator work; zero means save failed.</summary>
    [Parameter] public Func<Task<long>>? EnsureSaved { get; set; }
    /// <summary>Initial source reference from the editor.</summary>
    [Parameter] public string OcoReference { get; set; } = "";
    /// <summary>Current editor service list for comparison.</summary>
    [Parameter] public string[] Services { get; set; } = [];
    /// <summary>Explicit reviewed choice handed to the editor's save coordinator.</summary>
    [Parameter] public EventCallback<AnnouncementSourceApply> ApplyRequested { get; set; }
    /// <summary>Clears private editor state when a source request loses access.</summary>
    [Parameter] public EventCallback<UiProblem> AccessLost { get; set; }
    private MaintenanceProfileChoice[] _profiles = [];
    private AnnouncementSourceSubmission? _submission;
    private AnnouncementSourceJobStatus? _job;
    private AnnouncementSourceProposal? _proposal;
    private AnnouncementSourceReadiness? _readiness;
    private readonly HashSet<string> _fields = new(StringComparer.Ordinal);
    private CancellationTokenSource _pending = new();
    private UiProblem? _problem;
    private string _profile = "";
    private long _loadedVersion = -1;
    private bool _working, _services, _recipients, _disposed, _starting;
    private string _reviewedOffset = "+03:00";

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (_loadedVersion == Version || _starting)
        { return; }
        _loadedVersion = Version;
        await RefreshAsync();
    }
    private void ClearReview()
    { _proposal = null; _fields.Clear(); _services = false; _recipients = false; }
    private Task RefreshAsync() => ReadAsync(false);
    private Task RetryAsync() => ReadAsync(true);
    private async Task SubmitAsync()
    {
        if (_starting || _working || _submission is not null)
        { return; }
        _starting = true;
        try
        {
            long saved = EnsureSaved is null ? Version : await EnsureSaved();
            if (saved == 0)
            { return; }
            _submission = new(_profile, OcoReference, Guid.NewGuid().ToString("N"));
            await ReadAsync(true);
        }
        finally { _starting = false; }
    }
    private async Task ReadAsync(bool submit)
    {
        if (_disposed || submit && _submission is null)
        { return; }
        _pending.Cancel();
        _pending.Dispose();
        _pending = new();
        CancellationToken token = _pending.Token;
        _working = true;
        _problem = null;
        _readiness = null;
        _profiles = [];
        ClearReview();
        try
        {
            AnnouncementSourceReadiness readiness = await Api.SourceReadinessAsync(token);
            if (token.IsCancellationRequested)
            { return; }
            _readiness = readiness;
            MaintenanceProfileChoice[] choices = await Api.ProfilesAsync(token);
            if (token.IsCancellationRequested)
            { return; }
            _profiles = choices;
            if (Version == 0 && !submit)
            { return; }
            AnnouncementSourceJobStatus job = await Api.SourceJobAsync(DraftId, submit ? _submission : null, token);
            if (token.IsCancellationRequested)
            { return; }
            if (submit)
            { _submission = null; }
            _job = job;
            await InvokeAsync(StateHasChanged);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (!job.Terminal && await timer.WaitForNextTickAsync(token))
            {
                if (!await Js.InvokeAsync<bool>("secureOpsAnnouncements.visible", token))
                { continue; }
                job = await Api.SourceJobAsync(DraftId, null, token);
                if (token.IsCancellationRequested)
                { return; }
                _job = job;
                await InvokeAsync(StateHasChanged);
            }
            if (job.State is "Succeeded" or "Partial")
            {
                AnnouncementSourceProposal review = await Api.SourceProposalAsync(DraftId, job.JobId, token);
                if (!token.IsCancellationRequested)
                { _proposal = review; }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (JSDisconnectedException) { }
        catch (SecureOpsApiException ex)
        {
            if (token.IsCancellationRequested)
            { return; }
            if (ex.Problem.Kind is UiProblemKind.SessionExpired or UiProblemKind.Forbidden or UiProblemKind.AccessDisabled or UiProblemKind.AccessPending)
            { ClearReview(); _profiles = []; _job = null; _submission = null; await AccessLost.InvokeAsync(ex.Problem); }
            else if (ex.Problem.Code != "AnnouncementSourceJobNotFound")
            { _problem = ex.Problem; }
        }
        finally { if (!token.IsCancellationRequested) { _working = false; } }
    }
    private Task ApplyAsync()
    {
        if (Locked || _working || _proposal is not { Stale: false } review || review.DraftVersion != Version)
        { return Task.CompletedTask; }
        return ApplyRequested.InvokeAsync(new AnnouncementSourceApply(review.JobId, review.DraftVersion,
            [.. _fields], _recipients, _services, review.OverrideVersion)
        { ProfileFingerprint = review.ProfileFingerprint, ReviewedSourceOffset = review.ReviewedSourceOffset });
    }
    private async Task ReviewOffsetAsync()
    {
        if (_proposal is null || _working)
        { return; }
        _working = true;
        try
        { _proposal = await Api.SourceProposalAsync(DraftId, _proposal.JobId, _pending.Token, _reviewedOffset); _fields.Clear(); }
        catch (SecureOpsApiException exception) { _problem = exception.Problem; }
        catch (OperationCanceledException) { }
        finally { _working = false; }
    }
    private void Select(string field, bool selected)
    { if (selected) { _fields.Add(field); } else { _fields.Remove(field); } }
    private static string Label(string field) => AnnouncementForm.Fields.FirstOrDefault(f => f.Key == field).Label ?? field;
    private static string OriginLabel(string origin) => origin switch
    {
        "Profile" => "Bakım profili",
        "Submission" => "İncelenen OCO",
        "SourceStartDateProposal" => "Kaynak başlangıç günü önerisi",
        "SourceExplicitOffset" => "Kaynak ve açık UTC farkı",
        "SourceLocalText" => "Kaynak yerel saati",
        "OperatorReviewedOffset" => "Operatörün onayladığı UTC farkı",
        _ => "Kaynakta doğrulanmadı"
    };
    private static string IssueLabel(string? code) => code?.Split(':')[0] switch
    {
        null or "" => "",
        "CollectionHadNoDevices" => "Koleksiyonda sunucu bulunamadı",
        "AmbiguousDeviceRelationships" => "Birden fazla servis eşleşmesi var",
        "DevicesWithoutService" => "Servisi bulunamayan sunucular var",
        "DeviceRelationshipReadFailures" => "Bazı servis ilişkileri okunamadı",
        "ChangeWindowMissing" => "OCO çalışma tarihleri bulunamadı",
        "ChangeWindowAmbiguous" => "OCO için birden fazla kayıt bulundu",
        "ChangeWindowInvalid" => "OCO çalışma tarihleri geçersiz; bitiş başlangıçtan sonra olmalı",
        "ChangeWindowUnresolved" => "Kaynak tarihinin saat dilimi incelenmeli",
        "ChangeWindowUnavailable" or "ChangeWindowTimeout" => "OCO tarih kaynağına erişilemedi",
        "DeviceCeilingReached" or "DevicePageCeilingReached" => "Sorgu sınırına ulaşıldı; kapsam kısmi",
        "PartialCollectionErrors" => "Koleksiyon sorgusu kısmen tamamlandı",
        "DuplicateDevicesIgnored" => "Tekrarlanan sunucular birleştirildi",
        "MalformedDeviceRowsSkipped" => "Geçersiz sunucu satırları atlandı",
        "AnnouncementSourceCollectionUnavailable" => "Koleksiyon sorgusu başarısız; yönetici Worker sağlayıcı ayarlarını kontrol etmeli",
        "AnnouncementSourceConfigurationUnavailable" => "Kaynak yapılandırması eksik",
        "AnnouncementSourceAccessDenied" => "Kaynak sorgulama yetkisi yok",
        _ => "Kaynak işlemi tamamlanamadı; yönetici işlem kaydını kontrol etmeli"
    };
    private static string StateLabel(string state) => state switch
    {
        "Configured" => "Tanımlı",
        "Unconfigured" => "Yapılandırılmadı",
        "Invalid" => "Geçersiz",
        "Queued" => "Sırada",
        "Running" => "Sorgulanıyor",
        "Succeeded" => "Tamamlandı",
        "Partial" => "Kısmi sonuç",
        "Failed" => "Başarısız",
        "Unchanged" => "Aynı",
        "Changed" => "Değişiklik var",
        "RequiresOperatorOffset" => "Elle tarih incelemesi gerekli",
        "SourceUnavailable" => "Kaynak değeri yok",
        "Ready" => "İş kuyruğu hazır; kaynak bağlantısı henüz doğrulanmadı",
        "NoWorker" => "Güncel Worker kaydı yok; işler bekleyebilir",
        "WrongQueue" => "Çalışan Worker bu kuyruğu dinlemiyor",
        "ConfigurationMissing" => "Kaynak yapılandırması eksik",
        "Unknown" => "Kaynak durumu doğrulanamadı",
        "Disabled" => "Kaynak toplama kapalı",
        _ => state
    };
    /// <inheritdoc />
    public void Dispose() { _disposed = true; _pending.Cancel(); _pending.Dispose(); }
}
