using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using SecureOps.Domain.Announcements;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Announcements;
using SecureOps.Shared.Contracts.Resources;
using SecureOps.Ui.Services;

namespace SecureOps.Ui.Pages;

/// <summary>Explicit, owner-authorized draft journey; no source calls or mail sending.</summary>
public partial class Announcements
{
    /// <summary>Resets shell feedback when unsaved-edit protection cancels navigation.</summary>
    [Microsoft.AspNetCore.Components.CascadingParameter] public Shared.MainLayout? Shell { get; set; }
    private readonly CancellationTokenSource _lifetime = new();
    private AnnouncementPage? _page;
    private AnnouncementForm? _form;
    private AnnouncementContent? _savedContent;
    private ResourcePreferencesResponse? _preferences;
    private AnnouncementBanner[] _banners = [];
    private (AnnouncementContent Content, long Version)? _comparison;
    private Guid _id;
    private long _version;
    private bool _allowed, _busy, _dirty, _optional;
    private string? _html, _notice;
    private UiProblem? _problem;
    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    { Access.Changed += AccessChanged; await InitializeAsync(); }
    private Task InitializeAsync() => RunAsync(async () =>
    {
        AccessSnapshot access = await Access.GetAsync(_lifetime.Token);
        _allowed = access.Can(Capabilities.AnnouncementDrafts);
        if (!_allowed)
        { _problem = access.Problem ?? UiProblemFactory.FromResponse(403, null); return; }
        _page = await Api.ListAsync(1, _lifetime.Token);
        _preferences = await Resources.GetPreferencesAsync(_lifetime.Token);
    });
    private Task PageAsync(int number) => RunAsync(async () => _page = await Api.ListAsync(number, _lifetime.Token));
    private Task NewAsync() => RunAsync(async () =>
    {
        CancelPreview(true);
        _savedContent = null;
        _banners = await Api.BannersAsync(_lifetime.Token, "oco-table-v2");
        _id = Guid.NewGuid();
        _version = 0;
        _form = new();
        if (_banners.Count(b => b.State == "PresentNotValidated") == 1)
        { _form.Banner = _banners.Single(b => b.State == "PresentNotValidated").Revision; }
        _dirty = false;
        Add("To");
    });
    private Task OpenAsync(Guid id) => RunAsync(async () =>
    {
        CancelPreview(true);
        (AnnouncementContent Content, long Version) draft = await Api.DraftAsync(id, 0, null, _lifetime.Token);
        _banners = await Api.BannersAsync(_lifetime.Token, draft.Content.TemplateRevision);
        _id = id;
        _version = draft.Version;
        _form = AnnouncementForm.From(draft.Content);
        _savedContent = draft.Content;
        _dirty = false;
    });
    private Task BannersAsync() => RunAsync(async () => _banners = await Api.BannersAsync(_lifetime.Token, _form!.Template));
    private async Task UpgradeAsync()
    {
        if (await Dialogs.ShowMessageBox("Taslağı yükselt", "Düzenlemeler korunacak. Yeni biçim yalnızca Kaydet ile yeni sürüme işlenecek.", yesText: "Yükselt", cancelText: "Vazgeç") != true)
        { return; }
        _form!.Template = "oco-table-v2";
        _form.DateTextRevision = "tr-v1";
        _form.Banner = "";
        _banners = [];
        Changed();
        await BannersAsync();
    }
    private async Task<bool> DiscardAsync() => !_dirty || await Dialogs.ShowMessageBox("Kaydedilmemiş değişiklikler",
        "Değişiklikleri bırakıp devam edilsin mi?", yesText: "Değişiklikleri bırak", cancelText: "Düzenlemeye dön") == true;
    private async Task LeavingAsync(LocationChangingContext context)
    {
        if (_busy || !await DiscardAsync())
        {
            context.PreventNavigation();
            Shell?.CancelPendingNavigation();
        }
        else
        { CancelPreview(true); }
    }
    private async Task BackAsync()
    {
        if (!await DiscardAsync())
        {
            return;
        }

        _form = null;
        CancelPreview(true);
        _html = null;
        _comparison = null;
        _dirty = false;
        _optional = false;
        await PageAsync(1);
    }
    private void Change(string key, string value) { _form!.Values[key] = value; Changed(); }
    private void Changed()
    {
        _dirty = _savedContent is null || _form!.Differences(_savedContent).Any();
        _notice = null;
        if (_problem?.Kind == UiProblemKind.Validation)
        { _problem = null; }
        SchedulePreview();
    }
    private void Add(string kind) { _form!.Recipients.Add(new(kind, "")); Changed(); }
    private void Remove(AnnouncementForm.Recipient recipient) { _form!.Recipients.Remove(recipient); Changed(); }
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (_comparison is not null)
        { return; }
        (AnnouncementContent Content, long Version) saved = await Api.DraftAsync(_id, _version, _form!.Content(), _lifetime.Token);
        _form = AnnouncementForm.From(saved.Content);
        _savedContent = saved.Content;
        _version = saved.Version;
        _dirty = false;
        _notice = "Taslak kaydedildi.";
    });
    private Task PreviewAsync() => RunAsync(async () =>
    {
        if (_dirty || _version == 0)
        {
            return;
        }

        _previewTab = true;
        _html = _previewPolicy
            + await Api.PreviewAsync(_id, _version, _lifetime.Token);
        _htmlGeneration = _previewRequests.Generation;
    });
    private Task DownloadAsync() => RunAsync(async () =>
    {
        if (_dirty || _version == 0)
        {
            return;
        }

        (byte[] Bytes, string Name) file = await Api.DownloadAsync(_id, _version, _lifetime.Token);
        await Js.InvokeVoidAsync("secureOpsDownload", _lifetime.Token, file.Name, "message/rfc822", Convert.ToBase64String(file.Bytes));
        _notice = "Mail dosyası indirildi; gönderim yapılmadı.";
    });
    private Task CompareAsync() => RunAsync(async () => _comparison = await Api.DraftAsync(_id, 0, null, _lifetime.Token));
    private void AcceptComparison()
    { _version = _comparison!.Value.Version; _savedContent = _comparison.Value.Content; _comparison = null; _problem = null; Changed(); _notice = "Düzenlemeleriniz korunuyor. Kaydet ile onaylayın."; }
    private static string FieldLabel(string key) => key switch
    {
        "To" => "Alıcılar",
        "Cc" => "Bilgi",
        "BannerRevision" => "Görsel",
        "TemplateRevision" => "Duyuru biçimi",
        "AffectedServices" => "Etkilenen servisler",
        _ => AnnouncementForm.Fields.First(f => f.Key == key).Label
    };
    private async Task FocusAsync(string key)
    {
        _previewTab = false;
        SchedulePreview();
        _optional = AnnouncementForm.Fields.Any(f => f.Key == key && f.Optional) || _optional;
        await InvokeAsync(StateHasChanged);
        await Js.InvokeVoidAsync("secureOpsAnnouncements.focus", _lifetime.Token, "announcement-" + key);
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || _lifetime.IsCancellationRequested)
        {
            return;
        }

        _busy = true;
        CancelPreview();
        _problem = null;
        _notice = null;
        try
        { await action(); }
        catch (SecureOpsApiException ex) { if (!LoseAccess(ex.Problem)) { _problem = ex.Problem; } }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { _busy = false; if (_htmlGeneration != _previewRequests.Generation && _problem is null) { SchedulePreview(); } }
    }
    private void AccessChanged() => _ = InvokeAsync(async () =>
    { _allowed = (await Access.GetAsync(_lifetime.Token)).Can(Capabilities.AnnouncementDrafts); if (!_allowed) { _form = null; _savedContent = null; CancelPreview(true); } StateHasChanged(); });
    /// <inheritdoc />
    public void Dispose() { Access.Changed -= AccessChanged; CancelPreview(true); _lifetime.Cancel(); _lifetime.Dispose(); }
}
