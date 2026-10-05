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

/// <summary>One headline figure on the executive summary.</summary>
/// <param name="Label">What is counted.</param>
/// <param name="Value">The figure (same value as in the detail sections).</param>
/// <param name="Note">Short qualifier, e.g. "takvim günü".</param>
public sealed record ReportTile(string Label, long Value, string? Note = null);

/// <summary>Executive summary: headline tiles and a few short tables. Every figure is also present in a detail section.</summary>
/// <param name="Subtitle">Scope and period in one line.</param>
/// <param name="Tiles">Headline figures, rendered four per row.</param>
/// <param name="Blocks">Short tables (gMSA transition, teams, upcoming plans), each limited to a few rows.</param>
/// <param name="Charts">Charts drawn from the same payload (<see cref="ReportCharts"/>); renderers skip all-zero charts.</param>
public sealed record ReportDashboard(string Subtitle, IReadOnlyList<ReportTile> Tiles, IReadOnlyList<ReportSection> Blocks,
    IReadOnlyList<ReportChart>? Charts = null);

/// <summary>Render-neutral report built only from an immutable snapshot payload; XLSX and PDF render the same document.</summary>
public sealed record ReportDocument(string Title, IReadOnlyList<(string Label, string Value)> Header, IReadOnlyList<ReportSection> Sections, DateTimeOffset CreatedAt,
    ReportDashboard? Dashboard = null)
{
    /// <summary>Rows shown in each executive-summary table (the full lists stay in the detail sections).</summary>
    public const int DashboardRows = 10;

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
                Row("Tarihsiz mail (döneme girmez)", s.UndatedMails),
                Row("Taslak mail (sayılmaz)", s.DraftMailsExcluded),
                Row("Açık/incelenen teknik bulgu", s.OpenFindings)
            ]),
            new("Dönem uzlaşımı", ["Tanım", "Dönem içi", "Daha eski", "Dönem sonrası (kesim öncesi)", "Kesim sonrası (hariç)", "Tarihi bilinmeyen", "Toplam", "Uzlaşıyor"],
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
        ];
        sections.AddRange(VersionTwo(report));
        if (report.GmsaNames is { } names)
        {
            sections.Add(new("İstenen gMSA adları", ["Hesap", "Kaynak", "İstenen gMSA adı", $"Karakter (en çok {ServiceAccountGmsaName.Limit})", "Durum"],
                [.. names.Select(n => Row(n.Account, n.Source, n.RequestedName, n.Length, n.Status))]));
        }

        sections.Add(new("Tanımlar ve notlar", ["Not"], [.. report.Notes.Select(n => Row(n))]));
        string period = $"{report.WeekStart:dd.MM.yyyy} – {report.WeekEndExclusive.AddDays(-1):dd.MM.yyyy}";
        ReportDashboard dashboard = new($"{report.ScopeLabel} · {period}",
        [
            new("Tekil hesap", s.UniqueAccounts),
            new("Sorumlusu atanmış hesap", s.AccountsWithAssignedPerson),
            new("Açık talep", s.OpenRequests),
            new("Geciken iş", s.OverdueRequests, "takvim günü"),
            new("Tarihli açık plan", s.DatedOpenPlanRequests, "talep"),
            new("Gerçekleşen işlem bildirimi", s.PerformedActionReports, "tümü"),
            new("Doğrulanmış kapanış", s.VerifiedClosureAccounts, "tekil hesap"),
            new("gMSA bekleyen", report.Handover.GmsaPending, "devir kapsamı")
        ],
        [
            new("gMSA geçişi", ["Gösterge", "Değer"],
            [
                Row("Devir kapsamına bildirilen", report.Handover.Reported),
                Row("Kabul kanıtı olan", report.Handover.Accepted),
                Row("gMSA hedefli", report.Handover.GmsaTargeted),
                Row("gMSA geçişi gerçekleşen", report.Handover.GmsaCompleted),
                Row("gMSA bekleyen", report.Handover.GmsaPending),
                .. (report.Funnel?.Stages ?? []).Select(st => Row("Huni: " + st.Label, st.Count))
            ]),
            new("Ekiplerde hesap ve bekleyen iş", ["Ekip", "Sahip olduğu hesap", "Muhatap açık talep", "Geciken"],
                [.. report.TeamWorkload.OrderByDescending(t => t.OwnedAccounts).ThenBy(t => t.Team, StringComparer.Ordinal).Take(DashboardRows)
                    .Select(t => Row(t.Team, t.OwnedAccounts, t.OpenRequestsAsTarget, t.OverdueAsTarget))]),
            new("Yaklaşan planlar (bitişe göre)", ["Hesap", "Aksiyon", "Bitiş", "Muhatap ekip", "Gecikiyor"],
                [.. report.DatedPlans.OrderBy(p => p.End).ThenBy(p => p.Account, StringComparer.Ordinal).Take(DashboardRows)
                    .Select(p => Row(p.Account, p.Action, new ReportCell(Date: p.End), p.TargetTeam ?? "—", p.Overdue ? "Evet" : "Hayır"))])
        ], ReportCharts.Build(report));
        return new ReportDocument(report.Period switch
        {
            ReportPeriods.Month => "Servis Hesapları Aylık Rapor",
            ReportPeriods.Custom => "Servis Hesapları Dönem Raporu",
            _ => "Servis Hesapları Haftalık Rapor"
        },
        [
            ("Nüsha", label ?? snapshotId.ToString("D")),
            ("Kapsam", report.ScopeLabel),
            (report.Period == ReportPeriods.Week ? "Hafta" : "Dönem", $"{report.WeekStart:dd.MM.yyyy} – {report.WeekEndExclusive.AddDays(-1):dd.MM.yyyy}"
                + (report.Period == ReportPeriods.Week ? " (Pazartesi–Pazar, Europe/Istanbul)" : " (Europe/Istanbul iş günü tarihleri)")),
            ("Kesim (asOf)", ReportCalendar.LocalDate(report.AsOf).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) + " " + report.AsOf.ToString("HH:mm zzz", CultureInfo.InvariantCulture)),
            ("Metrik tanımı", report.MetricDefinitionVersion),
            ("Oluşturan / zaman", createdBy + " / " + createdAt.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)),
            ("Yük özeti (SHA-256)", payloadSha256)
        ], sections, createdAt, dashboard);
    }

    /// <summary>Metric version 2 sections; a version 1 snapshot has none and renders exactly as before.</summary>
    private static IEnumerable<ReportSection> VersionTwo(ServiceAccountReport report)
    {
        static IReadOnlyList<ReportCell> Row(params ReportCell[] cells) => cells;
        static ReportCell Date(DateOnly? value) => value is { } d ? new ReportCell(Date: d) : "—";
        if (report.Directorate is { } directorate)
        {
            yield return new("Direktörlük görünümü", ["Organizasyon", "Ekip", "Sahip olduğu hesap", "Muhatap açık talep", "Geciken", "Tarih bekleyen", "Kurala aykırı",
                "Bekleyen gMSA", "Risk adayı"],
                [.. directorate.Select(r => Row(r.Organization, r.Team, r.OwnedAccounts, r.OpenRequestsAsTarget, r.OverdueAsTarget, r.AwaitingDateAsTarget, r.AgainstRule,
                    r.GmsaPending, r.RiskCandidates))]);
        }

        if (report.Rules is { } rules)
        {
            yield return new("Bilgi bankası kuralları", ["Gösterge", "Değer"],
            [
                Row("Kural sürümü", rules.RuleSetVersion),
                Row("Değerlendirilen hesap", rules.Assessed),
                Row("Kurala aykırı (plansız) hesap", rules.AgainstRule),
                Row("Listede: kurala aykırı, bilgi eksik ve manuel inceleme bekleyen hesaplar", rules.Lines.Count),
                .. rules.ByPath.Where(p => p.Count > 0).Select(p => Row("  Önerilen yol: " + p.Label, p.Count)),
                .. rules.ByConformance.Select(c => Row("  Durum: " + c.Label, c.Count))
            ]);
            yield return new("İncelenecek hesaplar", ["Hesap", "Sahip ekip", "Önerilen yol", "Durum", "Kural", "Neden"],
                [.. rules.Lines.Select(l => Row(l.Account, l.OwnerTeam ?? "—", l.Path, l.Conformance, l.RuleCodes, l.Reason))]);
        }

        if (report.Funnel is { } funnel)
        {
            yield return new("gMSA hunisi", ["Aşama", "Hesap", "Beklenen"],
            [
                .. funnel.Stages.Select(st => Row(st.Label, st.Count, st.Expected)),
                Row("Uygun değil (huniden çıkış)", funnel.Ineligible, ""),
                Row("Toplam", funnel.Population, funnel.Note)
            ]);
        }

        if (report.Trend is { } trend)
        {
            yield return new("Trend (son haftalar)", ["Hafta başı", "Hafta sonu açık talep", "Hafta sonu geciken", "Doğrulanmış kapanış", "Gerçekleşen işlem"],
            [
                .. trend.Points.Select(p => Row(new ReportCell(Date: p.WeekStart), p.OpenAtWeekEnd, p.OverdueAtWeekEnd, p.VerifiedClosures, p.PerformedActions)),
                Row("Kapanış zamanı bilinmeyen talep (trende girmez)", trend.UnknownCloseTime, "", "", ""),
                Row(trend.Note, "", "", "", "")
            ]);
        }

        if (report.Risk is { } risk)
        {
            yield return new("Risk adayları", ["Hesap", "Sahip ekip", "Neden", "Kaynak tarihi", "Son oturum", "Son parola", "Açık talep"],
            [
                .. risk.Lines.Select(l => Row(l.Account, l.OwnerTeam ?? "—", l.Categories, Date(l.SourceDate), Date(l.LastLogon), Date(l.PasswordLastSet), l.OpenRequests)),
                Row(risk.Note, "", "", "", "", "", "")
            ]);
        }
    }
}
