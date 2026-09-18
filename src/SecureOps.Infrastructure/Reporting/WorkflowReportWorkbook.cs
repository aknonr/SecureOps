using System.Globalization;
using SecureOps.Infrastructure.InUse;
using SecureOps.Shared.Contracts.InUse;
using SecureOps.Shared.Contracts.Reporting;

namespace SecureOps.Infrastructure.Reporting;

/// <summary>Uses the same frozen filtered facts and safe inline-string writer as the operational workbook.</summary>
public static class WorkflowReportWorkbook
{
    /// <summary>Exports exact text identities; formulas, macros and external effects are never generated.</summary>
    public static byte[] Create(WorkflowReport report)
    {
        TimeSpan offset = report.Request.TimeZone == "UTC" ? TimeSpan.Zero : TimeSpan.FromHours(3);
        string At(DateTimeOffset value) => value.ToOffset(offset).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        List<IReadOnlyList<string>> context = [
            new[] { "Rapor kimliği", report.Id.ToString("D") }, new[] { "Kesit zamanı", At(report.AsOf) },
            new[] { "Dönem başlangıcı (dahil)", At(report.Request.From) }, new[] { "Dönem bitişi (hariç)", At(report.Request.To) },
            new[] { "Saat dilimi", report.Request.TimeZone }, new[] { "Demo / kökeni bilinmeyen kayıtlar", report.Request.IncludeSynthetic ? "Dahil" : "Hariç" },
            new[] { "Kapsam", "Yetkili In Use/OR kayıtları; yalnız kendi OCO kayıtlarınız" },
            new[] { "Modül", report.Filter.Module ?? "Tümü" }, new[] { "Durum", report.Filter.Status is { } status ? WorkflowReportText.State(status) : "Tümü" },
            new[] { "Tür", report.Filter.RecordType is { } type ? WorkflowReportText.Type(type) : "Tümü" }, new[] { "Ölçü", report.Filter.Metric ?? "Tümü" },
            new[] { "Uyarı", "Güncel durum ile dönem olayları ayrı ölçülerdir; toplam tamamlanma sayısı oluşturmaz" }];
        context.AddRange(report.Limitations.Select(x => (IReadOnlyList<string>)new[] { "Sınırlama", WorkflowReportText.Limitation(x) }));
        context.AddRange(report.Readiness.Select(x => (IReadOnlyList<string>)new[] { x.Module, WorkflowReportText.State(x.State), x.LastSuccess is { } at ? At(at) : "Bilinmiyor", x.Reason }));
        List<IReadOnlyList<string>> metrics = [new[] { "Ölçü", "Tanım", "Birim", "Zaman temeli", "Sayı" }];
        metrics.AddRange(report.Metrics.Select(m => (IReadOnlyList<string>)new[] { m.Key, m.Label, m.Unit, WorkflowReportText.Basis(m.TimeBasis), m.Count.ToString(CultureInfo.InvariantCulture) }));
        List<IReadOnlyList<string>> rows = [new[] { "Ölçü", "Belge kimliği", "Modül", "WASAS kayıt kimliği", "OR/OCO", "Tür", "Durum", "İşlemi başlatan / hazırlayan", "İnceleyici", "Zaman", "Sonuç / sonraki adım" }];
        rows.AddRange(report.Items.Select(r => (IReadOnlyList<string>)new[] { r.Metric, r.LogicalId, r.Module, r.RecordId.ToString("D"), r.Reference,
            WorkflowReportText.Type(r.RecordType), WorkflowReportText.State(r.Status), r.Actor ?? "Bilinmiyor", r.Assignee ?? "Atanmamış / uygulanmaz", r.OccurredAt is { } at ? At(at) : "Bilinmiyor", r.Detail ?? "" }));
        return InUseWorkbook.Write([new InUseSheet("Rapor", context), new("Olculer", metrics), new("Kayitlar", rows)]);
    }
}
