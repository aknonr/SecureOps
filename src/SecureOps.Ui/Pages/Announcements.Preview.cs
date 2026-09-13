using SecureOps.Domain.Announcements;
using SecureOps.Ui.Services;

namespace SecureOps.Ui.Pages;

public partial class Announcements
{
    private const string _previewPolicy = "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src data:; style-src 'unsafe-inline'\">";
    private readonly AnnouncementPreviewRequests _previewRequests = new();
    private bool _wide, _previewTab;
    private string _previewStatus = "";
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
    private void CancelPreview()
    {
        _previewRequests.Cancel();
        _html = null;
        _previewProblem = null;
    }
    private void SchedulePreview()
    {
        CancelPreview();
        if (!PreviewVisible || _form is null || !_allowed || _lifetime.IsCancellationRequested)
        { return; }
        _previewStatus = "Önizleme güncelleniyor";
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
            _html = _previewPolicy + result.Html;
            _previewStatus = "Kaydedilmemiş önizleme" + (result.Missing.Length > 0 ? $" · {result.Missing.Length} eksik alan" : "");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (SecureOpsApiException ex)
        {
            if (!_previewRequests.IsCurrent(sequence))
            { return; }
            _html = null;
            _previewStatus = "Önizleme güncellenemedi";
            _previewProblem = ex.Problem;
        }
        if (_previewRequests.IsCurrent(sequence))
        { await InvokeAsync(StateHasChanged); }
    }
}
