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
    private readonly HashSet<string> _fields = new(StringComparer.Ordinal);
    private CancellationTokenSource _pending = new();
    private UiProblem? _problem;
    private string _profile = "", _oco = "";
    private long _loadedVersion = -1;
    private bool _working, _services, _recipients, _disposed;

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (_loadedVersion == Version)
        { return; }
        _loadedVersion = Version;
        _oco = OcoReference;
        await RefreshAsync();
    }
    private void ClearReview()
    { _proposal = null; _fields.Clear(); _services = false; _recipients = false; }
    private Task RefreshAsync() => ReadAsync(false);
    private Task RetryAsync() => ReadAsync(true);
    private Task SubmitAsync()
    {
        if (Locked || Version == 0 || _submission is not null)
        { return Task.CompletedTask; }
        _submission = new(_profile, _oco, Guid.NewGuid().ToString("N"));
        return ReadAsync(true);
    }
    private async Task ReadAsync(bool submit)
    {
        if (_disposed || submit && (Locked || Version == 0))
        { return; }
        _pending.Cancel();
        _pending.Dispose();
        _pending = new();
        CancellationToken token = _pending.Token;
        _working = true;
        _problem = null;
        ClearReview();
        try
        {
            MaintenanceProfileChoice[] choices = await Api.ProfilesAsync(token);
            if (token.IsCancellationRequested)
            { return; }
            _profiles = choices;
            if (Version == 0)
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
            [.. _fields], _recipients, _services, review.OverrideVersion));
    }
    private void Select(string field, bool selected)
    { if (selected) { _fields.Add(field); } else { _fields.Remove(field); } }
    private static string Label(string field) => AnnouncementForm.Fields.FirstOrDefault(f => f.Key == field).Label ?? field;
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
        _ => state
    };
    /// <inheritdoc />
    public void Dispose() { _disposed = true; _pending.Cancel(); _pending.Dispose(); }
}
