using SecureOps.Shared.Contracts.Reporting;

namespace SecureOps.Infrastructure.Reporting;

/// <summary>Stable workflow units and Turkish meanings; independent stages are never summed.</summary>
public static class WorkflowMetricCatalog
{
    /// <summary>Versioned metric definitions consumed by API, dashboard and workbook.</summary>
    public static IReadOnlyList<WorkflowMetric> Definitions { get; } = Create();

    private static WorkflowMetric[] Create()
    {
        List<WorkflowMetric> metrics = [
            new("InUse.Backlog", "InUse", "Yerel inceleme bekleyen", "OR", "Current", 0),
            new("InUse.Open", "InUse", "Kaynakta açık gözlemlenmiş", "OR", "Current", 0),
            new("InUse.Assigned", "InUse", "İnceleyici atanmış", "OR", "Current", 0),
            new("InUse.Unassigned", "InUse", "İnceleyici atanmamış", "OR", "Current", 0),
            new("InUse.Reviewed", "InUse", "Güncel cevapları tamamlanmış", "OR", "Current", 0),
            new("InUse.Servers", "InUse", "Güncel cevapları tamamlanmış sunucu", "OR / sunucu", "Current", 0),
            new("InUse.Archived", "InUse", "Doğrulanmış arşiv", "Değişmez rapor", "PreparedAt", 0),
            new("InUse.Attachment", "InUse", "Kaynak eki doğrulanmış", "Tamamlama işlemi", "EvidenceAt", 0),
            new("InUse.Bpm", "InUse", "İş akışı yanıtı alınmış", "Tamamlama işlemi", "EvidenceAt", 0),
            new("InUse.Closed", "InUse", "OR kapanışı doğrulanmış", "OR", "EvidenceAt", 0),
            new("Sdm.Eligible", "Sdm", "Aktarıma uygun", "OR", "Current", 0),
            new("Sdm.Blocked", "Sdm", "Uygunluk / eşleme engeli", "OR", "Current", 0),
            new("Sdm.Linked", "Sdm", "Jira bağlantısı kayıtlı", "OR", "JiraCreatedAt", 0),
            new("Sdm.JiraOnly", "Sdm", "Yalnız Jira aktarımı", "OR", "JiraCreatedAt", 0),
            new("Sdm.Closed", "Sdm", "Kaynak kapanışı doğrulanmış", "OR", "ObservedAt", 0),
            new("Oco.Prepared", "Oco", "Hazırlanmış duyuru", "Değişmez hazırlık", "PreparedAt", 0)];
        foreach (string state in new[] { "Failed", "Unknown", "Unconfirmed", "Blocked", "Partial" })
        { metrics.Add(new("InUse." + state, "InUse", "Tamamlama: " + State(state), "Tamamlama işlemi", "Current", 0)); }
        foreach (string state in new[] { "Failed", "Unknown" })
        { metrics.Add(new("Sdm." + state, "Sdm", "Aktarım: " + State(state), "OR", "Current", 0)); }
        foreach (string state in new[] { "Queued", "Running", "Succeeded", "Partial", "Failed" })
        { metrics.Add(new("Oco.Source." + state, "Oco", "Kaynak: " + State(state), "Mantıksal kaynak işi", "SubmittedAt", 0)); }
        foreach (string kind in new[] { "SelfTest", "Send" })
        {
            foreach (string state in new[] { "Queued", "Dispatching", "Accepted", "Partial", "Failed", "Unknown", "Denied" })
            { metrics.Add(new($"Oco.{kind}.{state}", "Oco", (kind == "SelfTest" ? "Kendime deneme: " : "Dağıtım: ") + State(state), "Mantıksal gönderim", "CreatedAt", 0)); }
        }
        return [.. metrics];
    }

    /// <summary>Safe outcome presentation; acceptance is not delivery or authoritative closure.</summary>
    public static string State(string state) => state switch
    {
        "Queued" => "Kuyrukta",
        "Running" or "Dispatching" => "İşleniyor",
        "Succeeded" => "Tamamlandı",
        "Accepted" => "SMTP kabul etti",
        "Partial" => "Kısmi sonuç",
        "Failed" => "Başarısız",
        "Unknown" => "Sonuç belirsiz",
        "Denied" => "Yetki reddi",
        "Unconfirmed" => "Doğrulanmadı",
        "Blocked" => "Engelli",
        _ => state
    };
}
