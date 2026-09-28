using System.Globalization;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.Reporting;

/// <summary>Typed cell: text, integer or business date.</summary>
public sealed record ReportCell(string? Text = null, long? Number = null, DateOnly? Date = null)
{
    /// <summary>Plain text rendering used by the PDF and by reconciliation tests.</summary>
    public string Display => Number?.ToString(CultureInfo.InvariantCulture) ?? Date?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? Text ?? string.Empty;

    /// <summary>Text cell.</summary>
    public static implicit operator ReportCell(string? text) => new(text ?? string.Empty);

    /// <summary>Number cell.</summary>
    public static implicit operator ReportCell(int number) => new(Number: number);
}

/// <summary>One table section.</summary>
public sealed record ReportSection(string Title, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<ReportCell>> Rows);

/// <summary>Render-neutral report built only from an immutable snapshot payload; XLSX and PDF render the same document.</summary>
public sealed record ReportDocument(string Title, IReadOnlyList<(string Label, string Value)> Header, IReadOnlyList<ReportSection> Sections, DateTimeOffset CreatedAt)
{
    /// <summary>Builds the document from a stored report payload and snapshot metadata.</summary>
    public static ReportDocument From(ServiceAccountReport report, Guid snapshotId, string payloadSha256, string? label, DateTimeOffset createdAt, string createdBy)
    {
        ReportSummary s = report.Summary;
        WeeklyMovement w = report.Weekly;
        static IReadOnlyList<ReportCell> Row(params ReportCell[] cells) => cells;
        IReadOnlyList<ReportCell> Placement(string name, PlacementCounts c) =>
            Row(name, c.InPeriod, c.Earlier, c.LaterBeforeCutoff, c.AfterCutoff, c.UnknownDate, c.Total, c.Reconciles ? "Evet" : "HAYIR");
        List<ReportSection> sections =
        [
            new("Özet", ["Gösterge", "Değer"],
            [
                Row("Tekil hesap", s.UniqueAccounts),
                Row("Sahip ekibi belirli hesap", s.AccountsWithOwnerTeam),
                Row("Onaylı sorumlu kişisi olan hesap", s.AccountsWithAssignedPerson),
                Row("Tekil onaylı sorumlu kişi", s.UniqueResponsiblePersons),
                Row("Açık talep", s.OpenRequests),
                Row("Tarihli açık plan (talep)", s.DatedOpenPlanRequests),
                Row("Tarihli açık plan (tekil hesap)", s.DatedOpenPlanAccounts),
                Row("Tarih bekleyen açık talep", s.AwaitingDateRequests),
                Row("Geciken iş (takvim günü)", s.OverdueRequests),
                Row("Gerçekleşen işlem bildirimi (tümü)", s.PerformedActionReports),
                Row("Doğrulanmış kapanış (tekil hesap)", s.VerifiedClosureAccounts),
                Row("Geçerli mail (tarihli)", s.ValidMails),
                Row("Tarihsiz mail (haftaya girmez)", s.UndatedMails),
                Row("Taslak mail (sayılmaz)", s.DraftMailsExcluded),
                Row("Açık/incelenen teknik bulgu", s.OpenFindings)
            ]),
            new("Haftalık uzlaşım", ["Tanım", "Dönem içi", "Daha eski", "Dönem sonrası (kesim öncesi)", "Kesim sonrası (hariç)", "Tarihi bilinmeyen", "Toplam", "Uzlaşıyor"],
            [
                Placement("Gerçekleşen işlem bildirimi", w.Actions),
                Placement("Doğrulanmış kapanış (hesap)", w.VerifiedClosures),
                Placement("Mail (fiziksel, taslak hariç)", w.Mails),
                Row("Dönem içi giden / gelen mail", w.OutgoingMailsInPeriod, w.IncomingMailsInPeriod, "", "", "", "", "")
            ]),
            new("Dönem içi işlemler", ["Hesap", "İşlem", "Sonuç", "İşlem tarihi", "İşlemi yapan ekip"],
                [.. w.ActionsInPeriod.Select(a => Row(a.Account, a.Action, a.Result, a.Date is { } d ? new ReportCell(Date: d) : "Bilinmiyor", a.PerformerTeam ?? "—"))]),
            new("Ekip iş yükü", ["Ekip", "Sahip olduğu hesap", "Muhatap olduğu açık talep", "Geciken (muhatap)"],
                [.. report.TeamWorkload.Select(t => Row(t.Team, t.OwnedAccounts, t.OpenRequestsAsTarget, t.OverdueAsTarget))]),
            new("Tarihli açık planlar", ["Hesap", "Beklenen aksiyon", "Başlangıç", "Bitiş", "Muhatap ekip", "Gecikiyor"],
                [.. report.DatedPlans.Select(p => Row(p.Account, p.Action, new ReportCell(Date: p.Start), new ReportCell(Date: p.End), p.TargetTeam ?? "—", p.Overdue ? "Evet" : "Hayır"))]),
            new("Devir ve gMSA", ["Gösterge", "Değer"],
            [
                Row("Devir kapsamına bildirilen hesap", report.Handover.Reported),
                Row("Yetkili kabul kanıtı olan hesap", report.Handover.Accepted),
                Row("Reddedilen", report.Handover.Rejected),
                Row("gMSA hedefli", report.Handover.GmsaTargeted),
                Row("gMSA geçişi gerçekleşen", report.Handover.GmsaCompleted),
                Row("gMSA bekleyen", report.Handover.GmsaPending),
                .. report.Handover.PendingBySuitability.Select(p => Row("  Bekleyen – uygunluk: " + p.Label, p.Count))
            ]),
            new("Eski birleşik görünüm (mutabakat)", ["Gösterge", "Değer"],
            [
                Row("Eski görünüm: adı geçen sorumlulu hesap", report.Legacy.NamedAccounts),
                Row("Takipçi yedeğiyle gösterilen hesap (sahip değil)", report.Legacy.FollowupFallbackAccounts),
                Row("Eski birleşik görünüm hesap", report.Legacy.CombinedAccounts),
                Row("Eski birleşik görünüm kişi", report.Legacy.CombinedPeople),
                Row(report.Legacy.Label, "")
            ]),
            new("Tanımlar ve notlar", ["Not"], [.. report.Notes.Select(n => Row(n))])
        ];
        return new ReportDocument("Servis Hesapları Haftalık Rapor",
        [
            ("Nüsha", label ?? snapshotId.ToString("D")),
            ("Kapsam", report.ScopeLabel),
            ("Hafta", $"{report.WeekStart:dd.MM.yyyy} – {report.WeekEndExclusive.AddDays(-1):dd.MM.yyyy} (Pazartesi–Pazar, Europe/Istanbul)"),
            ("Kesim (asOf)", ReportCalendar.LocalDate(report.AsOf).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) + " " + report.AsOf.ToString("HH:mm zzz", CultureInfo.InvariantCulture)),
            ("Metrik tanımı", report.MetricDefinitionVersion),
            ("Oluşturan / zaman", createdBy + " / " + createdAt.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)),
            ("Yük özeti (SHA-256)", payloadSha256)
        ], sections, createdAt);
    }
}
