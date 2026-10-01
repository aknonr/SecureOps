using Microsoft.AspNetCore.Components;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Ui.Pages;

public partial class InUse
{
    private string _answerView = "missing";
    private readonly Dictionary<(string Server, string Check), ElementReference> _rowElements = [];
    private InUseServerHistory? _history;
    private string _historySearch = "";
    private int _historyPage = 1;
    private readonly HashSet<string> _reuseSelection = [];
    private InUseExecutionStatus? _execution;
    private bool MayStartExecution => _execution?.Operation is not { } operation
        || operation.Step == 0 && (operation.State is "Failed" or "Blocked") && operation.ReportVersion != _record?.Version;
    private async Task ReadExecutionAsync()
    { _execution = await ReadAsync<InUseExecutionStatus>($"/{_record!.Id}/execution"); }
    private Task RefreshExecutionAsync() => ExecuteAsync(ReadExecutionAsync);
    private Task StartExecutionAsync() => ExecuteAsync(async () =>
    {
        if (_record is null || _dirty || !Can(SecureOps.Shared.Auth.Capabilities.InUseComplete)
            || !MayStartExecution || _execution?.Readiness.Available != true || !Ready())
        { return; }
        InUseRecord selected = _record;
        int generation = _generation;
        _report = await SendAsync<InUseReport>(HttpMethod.Post, $"/{selected.Id}/report", new ExportInUseRequest(selected.Version, Archive: true));
        InUseReport reviewed = _report;
        if (await Dialogs.ShowMessageBox($"{selected.Source.Code}: WASAS adımını onayla",
            $"OR: {selected.Source.Code}. Başlatan: {_access?.Profile?.DisplayName ?? _access?.Profile?.Account ?? _access?.Access?.UserId.ToString("D")}. "
            + $"{selected.Source.Servers.Count} sunucu; rapor sürümü {reviewed.Version}; arşiv SHA-256 (16 karakterlik bloklar): {string.Join(" ", reviewed.Sha256.Chunk(16).Select(c => new string(c)))}. "
            + $"Kaynağa eklenecek dosya: {selected.Source.Code}_InUse.xlsx. Sunucu tipi: Application Server. Ortam: {RequiredEnvironment(selected)}. "
            + "Aynı baytlar eklenip ek içeriği doğrulandıktan sonra yalnız tek uygun WASAS aktivitesi onaylanacak. Başka ekibin görevi onaylanmaz; genel OR açık kalabilir. "
            + "WASAS adımını kaynak sistemde kontrol edin. İptal etmek yerel arşivi silmez. Dış etkiler geri alınamaz.",
            yesText: "Bu talep ve sürüm için onaylıyorum", cancelText: "Vazgeç") != true)
        { return; }
        if (generation != _generation || _record?.Id != selected.Id || _record.Version != selected.Version
            || _dirty || !Can(SecureOps.Shared.Auth.Capabilities.InUseComplete))
        { return; }
        InUseExecution operation = await SendAsync<InUseExecution>(HttpMethod.Post, $"/{selected.Id}/execution",
            new StartInUseExecutionRequest(Guid.NewGuid(), selected.Version, reviewed.Sha256));
        _execution = _execution with { Operation = operation };
        _notice = "İşlem kalıcı olarak kaydedildi. Ek, WASAS onayı ve genel OR durumunu ayrı ayrı izleyin.";
    });
    private Task ConfirmClosureAsync() => ExecuteAsync(async () =>
    {
        if (_record is null || !Can(SecureOps.Shared.Auth.Capabilities.InUseComplete)
            || _execution?.Operation is not { } operation || !InUseClosureVerification.CanConfirm(operation))
        { return; }
        int generation = _generation;
        bool activity = operation.VerificationMode == "WasasActivityManual";
        if (await Dialogs.ShowMessageBox(activity ? "WASAS adımı için manuel doğrulama" : "Geçmiş OR kapanışı için manuel doğrulama",
            $"{operation.SourceCode}: kaynak sistemde {(activity ? "WASAS aktivitesinin tamamlandığını" : "bu OR'nin kapalı olduğunu")} kontrol ettiniz mi? Onayınız kimliğiniz ve UTC zamanıyla manuel doğrulama olarak kaydedilir; sistem doğrulaması değildir ve yeni kaynak isteği göndermez.",
            yesText: "Kontrol ettim, manuel onayı kaydet", cancelText: "Vazgeç") != true)
        { return; }
        if (generation != _generation || _record?.Id != operation.RecordId || _execution?.Operation?.Revision != operation.Revision
            || !Can(SecureOps.Shared.Auth.Capabilities.InUseComplete))
        { return; }
        InUseExecution confirmed = await SendAsync<InUseExecution>(HttpMethod.Post, $"/{operation.RecordId}/execution/manual-verification",
            new ConfirmInUseClosureRequest(operation.OperationId, operation.Revision, operation.SourceCode));
        _execution = _execution with { Operation = confirmed };
        _notice = "Manuel doğrulama kaydedildi. Kaynak sisteme yeni istek gönderilmedi.";
    });
    private static string RequiredEnvironment(InUseRecord record)
    {
        string? environment = InUseRequiredFields.Environment(record.Source);
        return environment is null
            ? "Kaynak ortamını doğrulayın; bilinmeyen ortam TEST sayılmaz"
            : environment + " (iş kuralı; kaynak alan değeri eşlemesi ayrıca doğrulanmalı)";
    }
    private static string SourceActivity(InUseRecord record) => record.ActivityStatus switch
    {
        "Completed" => "Tamamlandığı kaynakta doğrulandı",
        "Pending" => "Kaynakta bekliyor",
        "OrClosed" => "Genel OR kapalı; WASAS aktivitesine ait doğrulama alınmadı",
        _ => "Doğrulama bekliyor; güncel WASAS durumu doğrulanmadı"
    };
    private static string SourceLifecycle(InUseRecord record) => string.IsNullOrWhiteSpace(record.Source.Lifecycle?.Source) ? "Kaynak durumu alınmadı" : record.Source.Lifecycle?.Value switch
    { "Open" => "Açık", "Closed" => "Kapalı (kaynak kanıtı)", _ => "Kaynak durumu alınmadı" };
    private static string ExecutionStep(string step) => step switch
    {
        "Intent" => "İncelenen rapor ve işlem kaydı",
        "Validate" => "Kaynak ve tekil görev kontrolü",
        "Property4463" => "Sunucu kategorisi",
        "Property4464" => "Ortam özelliği",
        "Upload" => "Rapor ekleme yanıtı",
        "Attachment" => "Ek kimliği ve bayt doğrulaması",
        "Bpm" => "WASAS aktivitesi onay yanıtı",
        "Closure" => "OR son durum okuması",
        "ManualVerification" => "Operatörün manuel doğrulaması",
        _ => "İşlem"
    };
    private static string ExecutionReason(string code) => code switch
    {
        "ServiceOrAspectMissing" => "Servis veya servis unsuru kimliği eksik. Kaynak eşlemesini doğrulatın; başka sunucunun bilgisini kullanmayın.",
        "MixedOrUnknownEnvironment" or "MixedEnvironment" => "Ortam bilinmiyor veya sunucular farklı ortamlarda. Talebin tek ortam değeri için onaylı eşleme gerekir.",
        "AccessRevoked" => "Başlatan kullanıcının tamamlama yetkisi artık geçerli değil. Kalan yazma adımları durduruldu.",
        "SourceOrReviewChanged" => "Kaynak veya incelenen cevaplar değişti. Önceki dış etkileri kontrol etmeden yeni işlem başlatmayın.",
        "ConfigurationChanged" => "İşlemden sonra yapılandırma değişti. Önceki sonucu yöneticinizle doğrulayın.",
        "AttachmentNotVerified" or "AttachmentRequired" => "Raporun doğru talebe eklendiği doğrulanamadı. Görev tamamlanmadı; eki tekrar yüklemeyin.",
        "OrStillOpen" => "Görev yanıtına rağmen OR kapalı durumu doğrulanmadı. Kaynak durumunu yetkili operatörle inceleyin.",
        "AlreadyClosed" => "Kaynak kayıt zaten kapalı. Yeni ek veya görev işlemi yapılmadı.",
        "AlreadyActivityCompleted" => "WASAS aktivitesi zaten tamamlanmış. Yeni ek veya başka ekibin görevi için onay gönderilmedi.",
        _ => "Kaydedilen adım sonuçlarını destek referansıyla yöneticinize iletin. Belirsiz yazma işlemini tekrar etmeyin; taslak ve arşiv korunur."
    };
    private static string ExecutionOutcome(string outcome) => outcome switch
    {
        "Verified" => "Kaynak okumasıyla doğrulandı",
        "Acknowledged" => "İstek kabul edildi; son durum doğrulanmadı",
        "ManuallyConfirmed" => "Manuel onay kaydedildi; sistem doğrulaması değil",
        "Started" => "Başladı",
        "Queued" => "Kaydedildi",
        "Rejected" => "Reddedildi",
        "ReadRetry" => "Salt okunur doğrulama yeniden sıraya alındı",
        "Blocked" => "Durduruldu",
        "Unconfirmed" => "Doğrulanamadı",
        _ => "Belirsiz; mutabakat gerekli"
    };
    private IEnumerable<InUseServer> AnswerServers => (_record?.Source.Servers ?? []).Where(s => _answerView == "all"
        || _answerView == "missing" && Remaining(s.Id) > 0 || _answerView == "changed" && InUseSourceChanges.Fields(s, _record?.Draft)?.Count > 0);
    private int Remaining(string id) => _answers.Count(a => a.ServerId == id && a.Value is not ("Yes" or "No"));
    private AnswerEdit? Edit(string id, string check) => _answers.SingleOrDefault(a => a.ServerId == id && a.Check == check);
    private string ServerName(string? id) => _record?.Source.Servers.FirstOrDefault(s => s.Id == id) is { } server ? Field(server, "HOSTNAME") : "Önceki sunucu";
    private void AnswerChanged(AnswerEdit answer)
    { answer.Origin = new("Individual"); Dirty(); }
    private string OriginText(InUseAnswerOrigin? origin) => origin is null ? "Önceki kayıt: cevap kökeni kaydedilmemiş"
        : (origin.Kind switch
        {
            "Bulk" => ServerName(origin.SourceServerId) + " sunucusundan kopyalandı",
            "PreviousReview" => "Önceki incelemeden seçildi",
            _ => "Tek tek yanıtlandı"
        }) + (origin.AcceptedAt is null ? " · kaydedilmedi" : $" · {origin.AcceptedByLabel ?? "Geçmiş profil adı yok"} · {Time(origin.AcceptedAt)}");
    private Task HistoryAsync(string id) => ExecuteAsync(async () =>
    {
        _editingServer = id;
        _changes = null;
        _reuseSelection.Clear();
        _history = await ReadAsync<InUseServerHistory>($"/{_record!.Id}/servers/{Uri.EscapeDataString(id)}/history?page={_historyPage}&pageSize=10&search={Uri.EscapeDataString(_historySearch)}");
    });
    private Task OpenHistoryAsync(string id) { _historyPage = 1; _historySearch = ""; return HistoryAsync(id); }
    private Task HistoryPageAsync(int page) { _historyPage = page; return HistoryAsync(_editingServer); }
    private void SelectReuse(Guid review, string check, ChangeEventArgs args)
    { string key = review + ":" + check; if (args.Value is true) { _reuseSelection.Add(key); } else { _reuseSelection.Remove(key); } }
    private async Task ReuseAsync(InUseServerReview review)
    {
        if (!CanEdit || _history?.Proposals.SingleOrDefault(p => p.ReviewId == review.Id)?.CanReuse != true)
        { return; }
        InUseAnswer[] selected = review.Answers.Where(a => _reuseSelection.Contains(review.Id + ":" + a.Check) && a.Value is "Yes" or "No").ToArray();
        if (selected.Length == 0 || await Dialogs.ShowMessageBox("Önceki cevapları kullan",
            $"{review.OrCode} / {Time(review.ReviewedAt)} tarihli {selected.Length} cevap bu sunucuya öneri olarak alınacak. Kaydederken yeniden doğrulanacak.",
            yesText: "Seçili cevapları al", cancelText: "Vazgeç") != true)
        { return; }
        foreach (InUseAnswer answer in selected)
        {
            if (Edit(_editingServer, answer.Check) is not { } target)
            { continue; }
            target.Value = answer.Value;
            target.Origin = new("PreviousReview", ReviewId: review.Id);
        }
        Dirty();
        _notice = "Seçtiğiniz önceki cevaplar taslakta; diğer cevaplar korunuyor. İnceleyip kaydedin.";
    }
}
