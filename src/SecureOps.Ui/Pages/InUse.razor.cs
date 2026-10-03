using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using MudBlazor;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.InUse;
using SecureOps.Ui.Services;
using SecureOps.Ui.Shared.Components;

namespace SecureOps.Ui.Pages;

/// <summary>API-authorized independent local In Use workspace.</summary>
public partial class InUse
{
    /// <summary>Optional persisted record identifier.</summary>
    [Parameter] public Guid? Id { get; set; }
    /// <summary>List context retained while inspecting a record and across reloads.</summary>
    [SupplyParameterFromQuery(Name = "q")] public string? QuerySearch { get; set; }
    /// <summary>Presentation filter only, never an ownership restriction.</summary>
    [SupplyParameterFromQuery(Name = "view")] public string? QueryView { get; set; }
    /// <summary>Saved review state filter.</summary>
    [SupplyParameterFromQuery(Name = "status")] public string? QueryStatus { get; set; }
    /// <summary>Source request creation ordering, retained across detail navigation.</summary>
    [SupplyParameterFromQuery(Name = "sort")] public string? QuerySort { get; set; }
    /// <summary>Stored bounded page.</summary>
    [SupplyParameterFromQuery(Name = "page")] public int? QueryPage { get; set; }
    /// <summary>Stored bounded page size.</summary>
    [SupplyParameterFromQuery(Name = "size")] public int? QuerySize { get; set; }
    private string _assigneeSearch = "";
    private AccessSnapshot? _access;
    private InUsePage? _page;
    private InUseRecord? _record;
    private InUseRecord? _comparison;
    private InUseReport? _report;
    private IReadOnlyList<InUseAssignee> _assignees = [];
    private UiProblem? _problem;
    private string? _notice;
    private string _search = "", _view = "all", _status = "", _sort = "code", _assignee = "", _reason = "", _notes = "";
    private string _editingServer = "";
    private string? _validation;
    private (AnswerEdit Target, string Before, string After)[]? _changes;
    private readonly ElementReference[] _answerElements = new ElementReference[3];
    private int? _focusAnswer;
    private bool _busy, _dirty, _reloadRequested;
    private bool _acceptPolicy;
    private IDialogReference? _assignmentDialog;
    private int _number = 1, _size = 25, _sheet;
    private int _generation;
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
    private bool CanEdit => Can(Capabilities.InUseReview) && _record is { Discarded: false };
    private bool AssignmentEdited => _record is not null && (!string.IsNullOrWhiteSpace(_reason)
        || _assignee != (_record.AssigneeId?.ToString("D") ?? ""));
    private bool HasUnsaved => _dirty || AssignmentEdited;
    private void ReviewServer(string id)
    {
        _answerView = "all";
        _editingServer = id;
        _changes = null;
        _focusAnswer = CanEdit ? 0 : null;
    }
    private int CompletedServers => _record?.Source.Servers.Count(s => InUseChecks.OperatorCodes.All(c =>
        _answers.Any(a => a.ServerId == s.Id && a.Check == c && a.Value is "Yes" or "No"))) ?? 0;
    private string NextAction => _record?.Discarded == true ? "Yerel taslak kaldırılmış. Yetkili kullanıcı Yeniden başla ile boş inceleme açabilir; arşiv korunur."
        : !CanEdit ? "İnceleme yetkisi olan bir kullanıcı devam edebilir; atama zorunlu değil."
        : _record!.Status == "Stale" ? "Değişen kaynak kanıtına göre cevapları yeniden doğrulayın."
        : _record.Draft is null || _dirty ? "Sunucu cevaplarını inceleyip yerel taslağı kaydedin."
        : CompletedServers < _record.Source.Servers.Count ? "Eksik cevapları tamamlayın; taslağınız korunuyor."
        : "Kaydedilen taslağın Excel önizlemesini ve talebe ekleme koşullarını kontrol edin.";

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    { AccessProvider.Changed += AccessChanged; _access = await AccessProvider.GetAsync(_lifetime.Token); }
    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        _search = QuerySearch is { Length: <= 100 } ? QuerySearch : "";
        _view = QueryView is "mine" or "unassigned" or "tracking" or "review" or "pending" or "verification" ? QueryView : "all";
        // Date ordering remains available in the API, but the real parent creation mapping is unverified.
        _sort = "code";
        _status = QueryStatus is "Unreviewed" or "Draft" or "Stale" or "Discarded" ? QueryStatus : "";
        _number = QueryPage is > 0 and <= 100000 ? QueryPage.Value : 1;
        _size = QuerySize is 10 or 25 or 50 ? QuerySize.Value : 25;
        if (_access is not null)
        { await LoadAsync(); }
    }
    private async Task InitializeAsync()
    { _access = await AccessProvider.RefreshAsync(_lifetime.Token); if (Can(Capabilities.InUseView)) { await LoadAsync(); } }
    private Task FilterAsync() { _number = 1; return NavigateListAsync(); }
    private Task SearchAsync(string value) { _search = value; return FilterAsync(); }
    private Task PreviousAsync() { _number = Math.Max(1, _number - 1); return NavigateListAsync(); }
    private Task NextAsync() { _number++; return NavigateListAsync(); }
    private string ListQuery => $"?q={Uri.EscapeDataString(_search)}&view={_view}&status={_status}&sort={_sort}&page={_number}&size={_size}";
    private string ListUrl => "/in-use" + ListQuery;
    private string RecordUrl(Guid id) => $"/in-use/{id}" + ListQuery;
    private Task NavigateListAsync() { Navigation.NavigateTo(ListUrl, replace: true); return Task.CompletedTask; }
    private Task SearchAssigneesAsync(string value) => ExecuteAsync(async () =>
    {
        _assigneeSearch = value;
        _assignees = await ReadAsync<InUseAssignee[]>("/assignees?search=" + Uri.EscapeDataString(value));
    });
    private static int SavedCompleted(InUseRecord record) => record.Source.Servers.Count(s => InUseChecks.OperatorCodes.All(c =>
        record.Draft?.Answers.Any(a => a.ServerId == s.Id && a.Check == c && a.Value is "Yes" or "No") == true));
    private static string RowNext(InUseRecord record) => record.TrackingOnly ? "Kaynakta doğrulanan sonucu inceleyin; yeni WASAS onayı göndermeyin"
        : record.HasActiveExecution ? "Önceki işlemin sonucunu doğrulayın; yeni onay göndermeyin"
        : record.SourceObservationMissing ? "Son yenilemede görülmedi; kaynak durumunu doğrulayın, cevaplar korunuyor"
        : record.Status == "Stale" ? "Sunucu incelemesini etkileyen farkları inceleyin; cevaplar korunuyor"
        : record.Source.Servers.Count == 0 ? "Sunucu ilişki kanıtı gerekli"
        : SavedCompleted(record) < record.Source.Servers.Count ? "Eksik cevapları tamamla" : "Excel önizlemesini incele";
    private Task LoadAsync()
    {
        ++_generation;
        if (_busy)
        { _reloadRequested = true; return Task.CompletedTask; }
        if (!Can(Capabilities.InUseView))
        { return Task.CompletedTask; }
        if (_record?.Id == Id && HasUnsaved)
        { return CompareAsync(); }
        return ExecuteAsync(async () =>
        {
            _report = null;
            if (_record?.Id != Id || Id is null)
            { ClearRecord(); }
            if (Id is Guid id)
            {
                InUseRecord record = await ReadAsync<InUseRecord>($"/{id}");
                if (Can(Capabilities.InUseAssign))
                { _assignees = await ReadAsync<InUseAssignee[]>("/assignees"); }
                SetRecord(record);
                if (CanEdit)
                { await ReadExecutionAsync(); }
            }
            else
            {
                _page = null;
                _page = await ReadAsync<InUsePage>($"?search={Uri.EscapeDataString(_search)}&view={_view}&sort={_sort}&page={_number}&pageSize={_size}"
                    + (string.IsNullOrEmpty(_status) ? "" : "&status=" + _status));
            }
        });
    }
    private Task RefreshAsync() => ExecuteAsync(async () =>
    {
        InUseRefreshState state = await SendAsync<InUseRefreshState>(HttpMethod.Post, "/refresh", new RefreshInUseRequest(Guid.NewGuid()));
        _notice = state.Issue is null ? "Sınırlı kaynak okuması tamamlandı." : RefreshIssue(state.Issue);
        _page = await ReadAsync<InUsePage>($"?page={_number}&pageSize={_size}&view={_view}&sort={_sort}&search={Uri.EscapeDataString(_search)}"
            + (string.IsNullOrEmpty(_status) ? "" : "&status=" + _status));
    });
    private async Task OpenAssignmentAsync()
    {
        if (_record is null || _busy || !Can(Capabilities.InUseAssign))
        { return; }
        InUseRecord original = _record;
        int generation = _generation;
        DialogParameters parameters = new()
        {
            { nameof(InUseAssignmentDialog.Record), original },
            { nameof(InUseAssignmentDialog.Suggest), (Func<Task<InUseReporterSuggestion>>)(() =>
                ReadAsync<InUseReporterSuggestion>($"/{original.Id}/reporter-suggestion")) },
            { nameof(InUseAssignmentDialog.Search), (Func<string, Task<InUseAssignee[]>>)(search =>
                ReadAsync<InUseAssignee[]>("/assignees?search=" + Uri.EscapeDataString(search))) },
            { nameof(InUseAssignmentDialog.Save), (Func<AssignInUseRequest, Task<InUseRecord>>)(request =>
                SendAsync<InUseRecord>(HttpMethod.Put, $"/{original.Id}/assignment", request)) }
        };
        IDialogReference dialog = await Dialogs.ShowAsync<InUseAssignmentDialog>("İnceleyici",
            parameters, new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true, BackdropClick = false, CloseOnEscapeKey = false });
        _assignmentDialog = dialog;
        DialogResult? result = await dialog.Result;
        _assignmentDialog = null;
        if (result is null || result.Canceled || result.Data is not InUseRecord updated || generation != _generation || _record?.Id != original.Id)
        { return; }
        if (_dirty)
        {
            // Assignment changes the version, not the local answers being reviewed.
            _record = updated;
            _assignee = updated.AssigneeId?.ToString("D") ?? "";
            _report = null;
        }
        else
        { SetRecord(updated); }
        _reason = "";
        _notice = "Yerel atama kaydedildi. Sunucu cevapları korunuyor.";
    }

    private Task AssignAsync() => ExecuteAsync(async () =>
    {
        if (_record is null)
        { return; }
        SetRecord(await SendAsync<InUseRecord>(HttpMethod.Put, $"/{_record.Id}/assignment",
            new AssignInUseRequest(_record.Version, Guid.TryParse(_assignee, out Guid userId) ? userId : null, _reason)));
        _reason = "";
        _notice = "Yerel atama kaydedildi.";
    });
    private Task SaveAsync() => ExecuteAsync(async () =>
    {
        if (_record is null)
        { return; }
        if (_record.Status == "Stale" && await Dialogs.ShowMessageBoxAsync("Değişen sunucu bilgilerini yeniden incele",
            "Kaynak değişiklikleri bölümündeki eski/yeni değerleri kontrol ettiniz mi? Kaydetmek mevcut cevapları yeni kaynak sürümü için onaylar; cevaplar kendiliğinden değiştirilmez.",
            yesText: "Farkları inceledim, cevapları kaydet", cancelText: "İncelemeye dön") != true)
        { return; }
        string assignee = _assignee;
        bool assignmentEdited = AssignmentEdited;
        SetRecord(await SendAsync<InUseRecord>(HttpMethod.Put, $"/{_record.Id}/draft",
            new SaveInUseDraftRequest(_record.Version, _record.SourceVersion,
                _answers.Select(a => new InUseAnswer(a.ServerId, a.Check, a.Value, a.Evidence) { Origin = a.Origin }).ToArray(), _notes)
            { ReviewedPolicyFingerprint = _acceptPolicy ? _record.PolicyProposal?.Fingerprint : null }));
        if (assignmentEdited)
        { _assignee = assignee; }
        _notice = "Yerel inceleme taslağı kaydedildi.";
    });
    private async Task UndoUnsavedAsync()
    {
        if (_record is null || _busy || !_dirty)
        { return; }
        Guid id = _record.Id;
        long version = _record.Version;
        if (await Dialogs.ShowMessageBoxAsync("Kaydedilmemiş değişiklikleri geri al",
            $"{_record.Source.Code}: yalnızca kaydedilmemiş cevaplar son kayıtlı taslağa dönecek. Arşiv ve kaynak kaydı değişmez.",
            yesText: "Geri al", cancelText: "Vazgeç") != true)
        { return; }
        if (_record?.Id != id || _record.Version != version || !CanEdit)
        { return; }
        SetRecord(_record);
        _notice = "Son kayıtlı taslak geri yüklendi; dış sisteme işlem yapılmadı.";
    }
    private Task CompareAsync() => ExecuteAsync(async () =>
    {
        if (_record is not null)
        { _comparison = await ReadAsync<InUseRecord>($"/{_record.Id}"); }
    });
    private async Task ChangeDraftAsync(string action)
    {
        if (_record is null || _busy || !Can(Capabilities.InUseReview))
        { return; }
        Guid id = _record.Id;
        long version = _record.Version;
        string label = action switch { "Discard" => "Taslağı kaldır", "Restart" => "Yeniden başla", _ => "Kayıtlı cevapları sıfırla" };
        if (await Dialogs.ShowMessageBoxAsync(label,
            $"{_record.Source.Code}: {_record.Source.Servers.Count} sunucunun cevapları ve kabul edilmiş önerileri temizlenecek. Önceki sürümler ve Excel arşivleri korunur. Kaynak kaydı silinmez, dış işlemler geri alınmaz.",
            yesText: label, cancelText: "Vazgeç") != true)
        { return; }
        await ExecuteAsync(async () =>
        {
            if (_record?.Id != id || _record.Version != version)
            { return; }
            SetRecord(await SendAsync<InUseRecord>(HttpMethod.Post, $"/{id}/draft-lifecycle", new ChangeInUseDraftRequest(version, action, label)));
            _notice = action == "Discard" ? "Yerel taslak kaldırıldı. Kaldırılmış taslaklar filtresinden yeniden başlayabilirsiniz; arşivler korunur."
                : "Yeni inceleme sürümü açıldı. Önceki deneme cevapları yeniden kullanılmayacak.";
        });
    }
    private void AcceptComparison()
    {
        if (_comparison is null || _record is null)
        { return; }
        AnswerEdit[] local = _answers.ToArray();
        bool answersEdited = _dirty && !_comparison.Discarded && _comparison.InvalidatedReviewsThrough < _record.Version, assignmentEdited = AssignmentEdited;
        string assignee = _assignee;
        SetRecord(_comparison);
        foreach (AnswerEdit answer in _answers.Where(_ => answersEdited))
        {
            AnswerEdit? retained = local.SingleOrDefault(a => a.ServerId == answer.ServerId && a.Check == answer.Check);
            if (retained is not null)
            { answer.Value = retained.Value; answer.Evidence = retained.Evidence; answer.Origin = retained.Origin; }
        }
        if (assignmentEdited)
        { _assignee = assignee; }
        _problem = null;
        _dirty = answersEdited;
        _notice = "Güncel sürüm alındı; yerel cevaplarınız korunuyor. Gösterilen farkları doğruladıktan sonra taslağı kaydedin.";
    }
    private static IEnumerable<(string Server, string Field, string Before, string After)> SourceChanges(InUseRecord old, InUseRecord current)
    {
        foreach (string id in old.Source.Servers.Select(s => s.Id).Union(current.Source.Servers.Select(s => s.Id)))
        {
            InUseServer? before = old.Source.Servers.SingleOrDefault(s => s.Id == id);
            InUseServer? after = current.Source.Servers.SingleOrDefault(s => s.Id == id);
            if (before is null || after is null)
            { yield return (id, "Servis öğesi", before is null ? "Yok" : "Var", after is null ? "Yok" : "Var"); }
            if (before?.RelatedRequestReporter != after?.RelatedRequestReporter)
            {
                static string Reporter(InUseRelatedRequestReporter? r) => r is null ? "Sorgulanmadı"
                    : $"{r.RfcReference} · {InUseDisplayText.Decode(r.Display)} · {r.UserReference} · {r.EffectiveState(DateTimeOffset.UtcNow)}";
                yield return (id, InUseRelatedRequestReporter.Label, Reporter(before?.RelatedRequestReporter), Reporter(after?.RelatedRequestReporter));
            }
            foreach (string field in (before?.Fields.Keys ?? []).Union(after?.Fields.Keys ?? []))
            {
                string left = before?.Fields.GetValueOrDefault(field) is { } b ? Evidence(b) : "Bilinmiyor";
                string right = after?.Fields.GetValueOrDefault(field) is { } a ? Evidence(a) : "Bilinmiyor";
                if (left != right)
                { yield return (id, field, left, right); }
            }
        }
    }
    private Task PreviewAsync() => ExecuteAsync(async () => { _report = await ReportAsync(); _sheet = 0; });
    private Task ConfirmCompletionAsync() => ExecuteAsync(async () =>
    {
        if (_record is null || _report is not { Archived: true } report || !Ready())
        {
            return;
        }

        SetRecord(await SendAsync<InUseRecord>(HttpMethod.Post, $"/{_record.Id}/completion-intent",
            new ConfirmInUseRequest(_record.Version, Guid.NewGuid(), report.Sha256)));
    });
    private static string Waiting(InUseRecord record) => InUseProgress.Created(record.Source, DateTimeOffset.UtcNow) is { } created
        ? $"Kaynak açılışından beri {(DateTimeOffset.UtcNow - created).Days} gün"
        : "Kaynak açılış tarihi bilinmiyor" + (record.FirstSeenAt is { } seen ? $" · WASAS ilk görülme: {seen.ToLocalTime():g} (kaynak yaşı değil)" : " · Yerel ilk görülme bilinmiyor");
    private Task IndexReportAsync(long version) => ExecuteAsync(async () =>
    {
        await SendAsync<IndexInUseReportsResult>(HttpMethod.Post, $"/{_record!.Id}/reports/index",
            new IndexInUseReportsRequest(_record.Version, [version]));
        _notice = $"Sürüm {version} rapor kataloğunda. Arşiv baytları değiştirilmedi.";
    });
    private Task DownloadAsync(long? archivedVersion = null) => ExecuteAsync(async () =>
    {
        if (archivedVersion is null && !Ready())
        { return; }
        _report = await SendAsync<InUseReport>(HttpMethod.Post, $"/{_record!.Id}/report",
            new ExportInUseRequest(_record.Version, Archive: archivedVersion is null, ArchivedVersion: archivedVersion));
        _record = _record with { ArchivedVersions = _record.ArchivedVersions.Append(_report.Version).Distinct().OrderDescending().ToArray() };
        _notice = "Rapor WASAS arşivinde korunuyor. İndirme konumunu tarayıcınız belirler.";
        try
        {
            await Js.InvokeVoidAsync("secureOpsDownload", _report.FileName,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Convert.ToBase64String(_report.Content));
        }
        catch (Exception exception) when (exception is JSException or TaskCanceledException)
        {
            _problem = UiProblemFactory.FromResponse(0, new ProblemDetailsPayload { Code = "InUseDownloadFailed" });
        }
    });
    private bool Ready()
    {
        _answerView = "all";
        InUseAnswer? missing = InUseChecks.Missing(_record!.Source, _answers.Select(a => new InUseAnswer(a.ServerId, a.Check, a.Value, "")).ToArray());
        if (missing is not null)
        {
            _editingServer = missing.ServerId;
            _validation = $"{Field(_record.Source.Servers.Single(s => s.Id == missing.ServerId), "HOSTNAME")} ({missing.ServerId}): {Check(missing.Check)} için Evet veya Hayır seçin. Bilinmiyor cevabı yalnızca taslak olarak kaydedilebilir.";
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
    {
        if (_focusAnswer is int index)
        {
            _focusAnswer = null;
            if (_rowElements.TryGetValue((_editingServer, InUseChecks.OperatorCodes[index]), out ElementReference element))
            { await element.FocusAsync(); }
        }
        if (Id is null && _page is not null && !_busy)
        {
            try
            { await Js.InvokeVoidAsync("secureOpsInUseScroll.restore"); }
            catch (JSDisconnectedException) { }
        }
    }
    private Task<InUseReport> ReportAsync() => SendAsync<InUseReport>(HttpMethod.Post, $"/{_record!.Id}/report",
        new ExportInUseRequest(_record.Version));
    private Task<T> ReadAsync<T>(string path) => CurrentAsync(Api.GetAsync<T>(path, _lifetime.Token));
    private Task<T> SendAsync<T>(HttpMethod method, string path, object body) => CurrentAsync(Api.SendAsync<T>(method, path, body, _lifetime.Token));
    private async Task<T> CurrentAsync<T>(Task<T> pending)
    {
        int generation = _generation;
        T result = await pending;
        if (generation != _generation || _lifetime.IsCancellationRequested)
        { throw new OperationCanceledException(); }
        return result;
    }
    private void SetRecord(InUseRecord record)
    {
        _record = record;
        _acceptPolicy = false;
        _comparison = null;
        _assignee = record.AssigneeId?.ToString("D") ?? "";
        _notes = record.Draft?.Notes ?? "";
        _dirty = false;
        _report = null;
        _selected.Clear();
        _changes = null;
        _validation = null;
        _editingServer = record.Source.Servers.FirstOrDefault()?.Id ?? "";
        _history = null;
        _reuseSelection.Clear();
        _rowElements.Clear();
        _answers = record.Source.Servers.SelectMany(server => InUseChecks.OperatorCodes.Select(check =>
        {
            InUseAnswer? saved = record.Draft?.Answers.FirstOrDefault(a => a.ServerId == server.Id && a.Check == check);
            return new AnswerEdit(server.Id, check) { Value = saved?.Value ?? "Unknown", Evidence = saved?.Evidence ?? "", Origin = saved?.Origin };
        })).ToList();
    }
    private void SelectServer(string id, ChangeEventArgs args)
    { if (args.Value is true) { _selected.Add(id); } else { _selected.Remove(id); } _changes = null; }
    private void PreviewBulk() => _changes = _answers.Where(a => _selected.Contains(a.ServerId))
        .Select(a => (Target: a, Before: a.Value, After: _answers.Single(s => s.ServerId == _editingServer && s.Check == a.Check).Value))
        .Where(c => c.Before != c.After).ToArray();
    private async Task ApplyBulk()
    {
        if (!CanEdit || _changes is null)
        { return; }
        (AnswerEdit Target, string Before, string After)[] changes = _changes;
        if (await Dialogs.ShowMessageBoxAsync("Seçili sunucuların cevaplarını değiştir",
            $"{changes.Select(c => c.Target.ServerId).Distinct().Count()} sunucuda {changes.Length} gösterilen cevap değişecek. Diğer cevaplar korunacak.",
            yesText: "Gösterilen değişiklikleri uygula", cancelText: "Vazgeç") != true)
        { return; }
        foreach ((AnswerEdit Target, string Before, string After) change in changes)
        {
            change.Target.Value = change.After;
            change.Target.Origin = new("Bulk", _editingServer) { CopiedValue = change.After };
        }
        Dirty();
        _notice = "Toplu cevaplar taslakta. Kaydetmeden önce sunucu bazında inceleyin.";
    }
    private void Dirty() { _dirty = true; _report = null; _changes = null; _validation = null; _comparison = null; }
    private async Task ExecuteAsync(Func<Task> operation)
    {
        if (_busy)
        { return; }
        _busy = true;
        int generation = _generation;
        _problem = null;
        _notice = null;
        try
        { await operation(); }
        catch (SecureOpsApiException ex)
        {
            if (generation == _generation && !_lifetime.IsCancellationRequested)
            {
                _problem = ex.Problem;
                _report = null;
                if (ex.Problem.Kind is UiProblemKind.Forbidden or UiProblemKind.AccessDisabled or UiProblemKind.AccessPending or UiProblemKind.SessionExpired)
                { ++_generation; ClearRecord(); _access = new(null, ex.Problem, DateTimeOffset.UtcNow); }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested || generation != _generation) { }
        finally { _busy = false; }
        if (_reloadRequested && !_lifetime.IsCancellationRequested)
        { _reloadRequested = false; await LoadAsync(); }
    }
    private void AccessChanged() => InvokeAsync(async () =>
    {
        await RevalidateAccessAsync();
        StateHasChanged();
    });
    private async Task RevalidateAccessAsync()
    {
        AccessSnapshot access = await AccessProvider.GetAsync(_lifetime.Token);
        if (_lifetime.IsCancellationRequested)
        { return; }
        bool changed = _access is not null && (_access.Access?.UserId != access.Access?.UserId
            || _access.Access?.Version != access.Access?.Version || _access.Status != access.Status
            || !_access.Capabilities.Order().SequenceEqual(access.Capabilities.Order()));
        _access = access;
        if (changed)
        { ++_generation; _reloadRequested = false; ClearRecord(); if (Can(Capabilities.InUseView)) { await LoadAsync(); } }
    }
    private void ClearRecord()
    {
        _assignmentDialog?.Close();
        _assignmentDialog = null;
        _record = null;
        _page = null;
        _report = null;
        _comparison = null;
        _history = null;
        _execution = null;
        _reuseSelection.Clear();
        _rowElements.Clear();
        _assignees = [];
        _answers.Clear();
        _selected.Clear();
        _reason = _assignee = _notes = _editingServer = "";
        _dirty = false;
        _changes = null;
        _notice = _validation = null;
    }
    private async Task BeforeNavigationAsync(LocationChangingContext context)
    {
        // Authentication termination cannot wait for the command that detected it.
        if (Navigation.ToAbsoluteUri(context.TargetLocation).GetLeftPart(UriPartial.Path) == Navigation.ToAbsoluteUri("session-expired").GetLeftPart(UriPartial.Path))
        { ++_generation; ClearRecord(); return; }
        if (_busy)
        { context.PreventNavigation(); _notice = "İşlem sürüyor. Sonucu gördükten sonra sayfadan ayrılabilirsiniz."; return; }
        if (HasUnsaved && await Dialogs.ShowMessageBoxAsync("Kaydedilmemiş değişiklikler",
            "Kaydedilmemiş cevaplar ve atama gerekçesi silinecek.", yesText: "Ayrıl", cancelText: "Sayfada kal") != true)
        { context.PreventNavigation(); }
    }
    /// <inheritdoc />
    public void Dispose() { AccessProvider.Changed -= AccessChanged; ++_generation; _lifetime.Cancel(); _lifetime.Dispose(); }
    private static string Evidence(InUseEvidence evidence) => (evidence.Value ?? "Bilinmiyor") + " · " + evidence.Source;
    private static string BusinessEvidence(InUseEvidence evidence) => string.IsNullOrWhiteSpace(evidence.Value)
        ? "Kaynak eşlemesi henüz doğrulanmadı" : InUseDisplayText.Decode(evidence.Value);
    private static string ReporterSummary(InUseRecord record) => string.Join("; ", record.Source.Servers
        .Select(s => s.RelatedRequestReporter).Where(r => r is not null)
        .Select(r => InUseDisplayText.Decode(r!.Display)).Distinct(StringComparer.Ordinal).Take(3)) is { Length: > 0 } names
        ? names : "Bildiren bilgisi henüz doğrulanmadı";
    private static string PolicyField(string field) => field switch
    {
        "check:NmsRequested" => "NMS kapsamı",
        "check:MemoryAlarm" => "Bellek alarmı",
        "check:CpuAlarm" => "CPU alarmı",
        "check:UpDownAlarm" => "Erişilebilirlik alarmı",
        "check:DiskAlarm" => "Disk alarmı",
        "COUNTRY" => "Ülke",
        "Department" => "Bölüm",
        "Sub_Department" => "Ekip",
        "Contact_email" => "İletişim e-postası",
        "ITMC_Event_Owner_Group" => "Olay sorumlu grubu",
        "Device_Type" => "Cihaz türü",
        _ => field
    };
    private static string PolicyOrigin(string origin) => origin switch
    {
        "EnvironmentPolicy" => "Ortam kuralı önerisi",
        "MonitoringProposal" => "İzleme önerisi",
        "ConfiguredProposal" => "Yönetici tanımlı öneri",
        "Source" => "Kaynak verisi",
        "UnresolvedEnvironment" => "Ortam sınıflandırması gerekli",
        _ => "Onaylı yapılandırma gerekli"
    };
    private static string CompletionStatus(InUseCompletion intent) => intent.Stage switch
    {
        "Blocked" => "Tamamlama: sözleşme nedeniyle engelli. Ek yüklenmedi; görev tamamlanmadı; OR son durumu doğrulanmadı.",
        "UploadPending" => "Ek yükleme sonucu bekleniyor; yeniden yazma başlatmayın.",
        "UploadFailed" => "Ek yükleme başarısız. Görev tamamlanmadı; kayıtlı hatayı inceleyin.",
        "TaskLookupPending" => "Ek yüklendi; benzersiz yetkili görev eşleşmesi bekleniyor.",
        "TaskLookupBlocked" => "Ek yüklendi; görev eşleşmesi yok veya belirsiz. Görev tamamlanmadı.",
        "CompletionPending" => "Görev tamamlama sonucu bekleniyor; yeniden yazma başlatmayın.",
        "CompletionFailed" => "Ek yüklendi; görev tamamlama başarısız. OR kapanışı doğrulanmadı.",
        "VerificationPending" => "Görev tamamlandı; yetkili kaynaktan OR son durumu henüz doğrulanmadı.",
        "TaskCompletedOrOpen" => $"Görev tamamlandı; OR durumu: {intent.FinalOrState}. OR kapalı olarak doğrulanmadı.",
        "ClosedVerified" => "Görev tamamlandı; OR kapalı durumu kaynak okumasıyla doğrulandı.",
        _ => "Sonuç belirsiz; mutabakat gerekli. Dış yazmayı otomatik tekrarlamayın."
    };
    private static string Field(InUseServer server, string field) => server.Fields.GetValueOrDefault(field)?.Value is { } value
        ? InUseDisplayText.Field(field, value) : "Bilinmiyor";
    private static string FieldState(InUseServer server, string field) => !server.Fields.TryGetValue(field, out InUseEvidence? evidence)
        ? "Sorgulanmadı" : evidence.Source.StartsWith("Missing response cell:", StringComparison.Ordinal)
        ? "Yanıtta alan yok" : evidence.Source.StartsWith("Prior value retained;", StringComparison.Ordinal)
        ? Field(server, field) + " (önceki veri)" : string.IsNullOrWhiteSpace(evidence.Value)
        ? evidence.Source.StartsWith("TuruncuHat:", StringComparison.Ordinal) ? "Kaynakta boş" : "Doğrulanmadı" : Field(server, field);
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
        public InUseAnswerOrigin? Origin { get; set; }
    }
}
