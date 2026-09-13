using SecureOps.Domain.Announcements;
using SecureOps.Ui.Services;

namespace SecureOps.Ui.Pages;

public partial class Announcements
{
    private const string _previewPolicy = "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src data:; style-src 'unsafe-inline'\">";
    private readonly AnnouncementPreviewRequests _previewRequests = new();
    private bool _wide, _previewTab;
    private bool _previewUpdating, _previewStale;
    private long _htmlGeneration = -1;
    private IReadOnlyList<string> _previewFields = [];
    private string PreviewStatus => _previewUpdating ? "Önizleme güncelleniyor"
        : _previewFields.Count > 0 ? "Güncel alanlar eksik veya geçersiz"
        : _previewProblem is not null ? "Önizleme güncellenemedi"
        : _previewStale ? "Önizleme güncel değil"
        : _dirty || _version == 0 ? "Kaydedilmemiş önizleme" : "Kaydedilmiş önizleme";
    private UiProblem? _previewProblem;
    private bool PreviewVisible => _wide || _previewTab;
    private void WidthChanged(bool wide)
    {
        bool wasVisible = PreviewVisible;
        _wide = wide;
        if (wasVisible != PreviewVisible)
        { SchedulePreview(); }
    }
    private void PreviewTab(bool preview) { _previewTab = preview; SchedulePreview(); }
    private void CancelPreview(bool clear = false)
    {
        _previewRequests.Cancel();
        _previewUpdating = false;
        _previewStale = true;
        _previewProblem = null;
        _previewFields = [];
        if (clear)
        { _html = null; }
    }
    private void SchedulePreview()
    {
        CancelPreview();
        if (!PreviewVisible || _form is null || !_allowed || _lifetime.IsCancellationRequested)
        { return; }
        _previewUpdating = true;
        (long generation, CancellationToken token) = _previewRequests.Begin(_lifetime.Token);
        _ = RenderLiveAsync(_form.Content(), generation, token);
    }
    private async Task RenderLiveAsync(AnnouncementContent content, long sequence, CancellationToken token)
    {
        try
        {
            await Task.Delay(600, token);
            (string Html, string[] Missing) result = await Api.LiveAsync(content, token);
            if (!_previewRequests.IsCurrent(sequence))
            { return; }
            _previewFields = result.Missing;
            if (result.Missing.Length == 0 || _html is null)
            { _html = _previewPolicy + result.Html; _htmlGeneration = sequence; }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (SecureOpsApiException ex)
        {
            if (!_previewRequests.IsCurrent(sequence))
            { return; }
            if (LoseAccess(ex.Problem))
            { await InvokeAsync(StateHasChanged); return; }
            _previewProblem = ex.Problem;
            _previewFields = ex.Problem.Fields;
        }
        if (_previewRequests.IsCurrent(sequence))
        { _previewUpdating = false; await InvokeAsync(StateHasChanged); }
    }
    private void PreviewPresented(long generation)
    { if (_previewRequests.Generation == generation) { _previewStale = false; } }
    private string? FieldError(string key) => _previewFields.Contains(key) || _problem?.Fields.Contains(key) == true
        ? AnnouncementFieldFeedback.Message(key, _form!.Values) : null;
    private bool LoseAccess(UiProblem problem)
    {
        if (problem.Kind is not (UiProblemKind.SessionExpired or UiProblemKind.AccessDisabled or UiProblemKind.AccessPending or UiProblemKind.Forbidden))
        { return false; }
        _allowed = false;
        _form = null;
        _savedContent = null;
        _problem = problem;
        CancelPreview(true);
        return true;
    }
}
