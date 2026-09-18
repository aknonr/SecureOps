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
        if (_record is null || _dirty || !MayStartExecution || _execution?.Readiness.Available != true || !Ready())
        { return; }
        if (await Dialogs.ShowMessageBox("Talebe ekle ve tamamla",
            $"Başlatan: {_access?.Profile?.DisplayName ?? _access?.Profile?.Account ?? _access?.Access?.UserId.ToString("D")}. {_record.Source.Code}: kaydedilen {_record.Source.Servers.Count} sunuculuk rapor arşivlenecek; aynı baytlar talebe eklenecek, doğrulanan tek görev tamamlanacak ve OR son durumu ayrıca okunacak. İnceleyici ataması bu aktörün yerine geçmez. Dış etkiler tek işlemle geri alınamaz.",
            yesText: "Bu talep ve sürüm için onaylıyorum", cancelText: "Vazgeç") != true)
        { return; }
        _report = await SendAsync<InUseReport>(HttpMethod.Post, $"/{_record.Id}/report", new ExportInUseRequest(_record.Version, Archive: true));
        InUseExecution operation = await SendAsync<InUseExecution>(HttpMethod.Post, $"/{_record.Id}/execution",
            new StartInUseExecutionRequest(Guid.NewGuid(), _record.Version, _report.Sha256));
        _execution = _execution with { Operation = operation };
        _notice = "İşlem kalıcı olarak kaydedildi. Ek ve kapanış sonuçlarını ayrı ayrı izleyin.";
    });
    private static string ExecutionState(string state) => state switch
    {
        "Queued" => "İş kuyruğunda; Worker bekleniyor",
        "Running" => "Kaynak adımı çalışıyor; yeniden başlatmayın",
        "Completed" => "Ek doğrulandı ve OR kapalı durumu kaynaktan doğrulandı",
        "Unknown" => "Sonuç belirsiz. Kaynak mutabakatı gerekli; yazmayı tekrarlamayın",
        "Unconfirmed" => "Kaynak son durumu doğrulanamadı; OR kapatıldı sayılmaz",
        "Blocked" => "Yetki, yapılandırma veya kaynak sürümü değişti; kalan adımlar durduruldu",
        _ => "Adım başarısız; önceki başarılı etkiler korunuyor. Yeniden yazmadan sonucu inceleyin"
    };
    private static string ExecutionStep(string step) => step switch
    {
        "Intent" => "İncelenen rapor ve işlem kaydı",
        "Validate" => "Kaynak ve tekil görev kontrolü",
        "Property4463" => "Sunucu kategorisi",
        "Property4464" => "Ortam özelliği",
        "Upload" => "Rapor ekleme yanıtı",
        "Attachment" => "Ek kimliği ve bayt doğrulaması",
        "Bpm" => "Görev tamamlama yanıtı",
        "Closure" => "OR son durum okuması",
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
        _ => "Kaydedilen adım sonuçlarını destek referansıyla yöneticinize iletin. Belirsiz yazma işlemini tekrar etmeyin; taslak ve arşiv korunur."
    };
    private static string ExecutionOutcome(string outcome) => outcome switch
    {
        "Verified" => "Kaynak okumasıyla doğrulandı",
        "Acknowledged" => "İşlem yanıtı alındı; son durum ayrıca okunacak",
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
