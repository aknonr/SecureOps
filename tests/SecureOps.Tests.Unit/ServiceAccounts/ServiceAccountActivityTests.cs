using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.Import;
using SecureOps.Infrastructure.ServiceAccounts.Reporting;

namespace SecureOps.Tests.Unit.ServiceAccounts;

/// <summary>First-page activity section: classification, period filter, verified vs reported, and the XLSX/PDF first page.</summary>
public sealed class ServiceAccountActivityTests
{
    private static readonly DateTimeOffset _asOf = new(2026, 10, 16, 12, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateOnly _start = new(2026, 10, 10);
    private static readonly DateOnly _endExclusive = new(2026, 10, 17);
    private static readonly DateTimeOffset _created = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid _teamA = Guid.NewGuid(), _teamB = Guid.NewGuid(), _performer = Guid.NewGuid();

    private static readonly Dictionary<string, AccountFact> _accounts = new[] { "DEL_V", "DEL_R", "GMSA_IN", "GMSA_OLD", "HAND", "OWN_MOVE", "OWN_FIRST", "PWD", "PLAN", "REVIEW", "VOID", "NODATE", "FUTURE" }
        .ToDictionary(n => n, n => new AccountFact(Guid.NewGuid(), "SYN\\" + n, _teamA, null));

    private static ActionFact Action(string account, ServiceAccountActionType type, ServiceAccountActionResult result, DateOnly? on, DateOnly? verifiedOn = null,
        bool verifier = false, bool evidence = true, bool or = false, bool closure = false, bool voided = false) =>
        new(Guid.NewGuid(), _accounts[account].Id,
            new ActionFacts(type, result, closure ? ServiceAccountRecordKind.Closure : ServiceAccountRecordKind.Intermediate, on, verifiedOn, verifier, evidence, or, voided),
            on is null ? TimePrecision.Unknown : TimePrecision.DateOnly, null, _performer);

    private static ReportFacts Facts(ActivityFacts? activity = null)
    {
        DateOnly d(int day) => new(2026, 10, day);
        ActionFact[] actions =
        [
            // Verified closure of a deletion in the period (OR present): deleted + verified, and the account closure counts as a change.
            Action("DEL_V", ServiceAccountActionType.Deletion, ServiceAccountActionResult.Verified, d(11), d(12), verifier: true, or: true, closure: true),
            // Deletion only reported in the period: deleted, not verified, also pending.
            Action("DEL_R", ServiceAccountActionType.Deletion, ServiceAccountActionResult.Performed, d(13)),
            Action("GMSA_IN", ServiceAccountActionType.GmsaConversion, ServiceAccountActionResult.Performed, d(14)),
            // Before the period and still unverified: pending only.
            Action("GMSA_OLD", ServiceAccountActionType.GmsaConversion, ServiceAccountActionResult.Performed, d(1)),
            Action("PWD", ServiceAccountActionType.PasswordChange, ServiceAccountActionResult.Performed, d(15)),
            // Not changes: a review, a voided deletion, an undated deletion, one after the cut-off.
            Action("REVIEW", ServiceAccountActionType.Review, ServiceAccountActionResult.Performed, d(12)),
            Action("VOID", ServiceAccountActionType.Deletion, ServiceAccountActionResult.Performed, d(12), voided: true),
            Action("NODATE", ServiceAccountActionType.Deletion, ServiceAccountActionResult.Performed, null),
            Action("FUTURE", ServiceAccountActionType.Deletion, ServiceAccountActionResult.Performed, d(20))
        ];
        return new ReportFacts([.. _accounts.Values], [], actions, [], [], [], [], new Dictionary<Guid, string> { [_teamA] = "SYN EKIP A", [_teamB] = "SYN EKIP B", [_performer] = "SYN YAPAN" },
            Activity: activity ?? Inputs());
    }

    private static ActivityFacts Inputs() => new(
        [
            new ActivityOwnershipFact(_accounts["OWN_MOVE"].Id, _teamB, _teamA, new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.FromHours(3)), true),
            new ActivityOwnershipFact(_accounts["OWN_FIRST"].Id, _teamB, null, new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.FromHours(3)), false),
            new ActivityOwnershipFact(_accounts["PLAN"].Id, _teamA, _teamB, new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.FromHours(3)), true)
        ],
        [new ActivityHandoverFact(_accounts["HAND"].Id, _teamA, _teamB, new DateOnly(2026, 10, 12), true)],
        [new ActivityPlanFact(_accounts["PLAN"].Id, new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.FromHours(3)))],
        true);

    private static ServiceAccountReport Report(ReportFacts? facts = null, DateOnly? start = null, DateOnly? end = null) =>
        ServiceAccountMetrics.Compute(facts ?? Facts(), start ?? _start, end ?? _endExclusive, _asOf, "SYN", ReportPeriods.Custom);

    private static ActivityGroup Group(ServiceAccountReport report, string key) => report.Activity!.Groups.Single(g => g.Key == key);

    private static string[] Accounts(ActivityGroup group) => [.. group.Items.Select(i => i.Account)];

    [Fact]
    public void Classifies_DeletedHandedGmsaAndChanged_AndKeepsReportedApartFromVerified()
    {
        ServiceAccountReport report = Report();

        Accounts(Group(report, ActivityGroupKeys.Deleted)).Should().BeEquivalentTo("SYN\\DEL_V", "SYN\\DEL_R");
        Group(report, ActivityGroupKeys.Deleted).Items.Single(i => i.Account == "SYN\\DEL_V").Status.Should().StartWith("Doğrulandı");
        ActivityLine reported = Group(report, ActivityGroupKeys.Deleted).Items.Single(i => i.Account == "SYN\\DEL_R");
        reported.Status.Should().Be("Bildirildi · doğrulanmadı");
        reported.Evidence.Should().Be("Var · OR yok");
        Accounts(Group(report, ActivityGroupKeys.Gmsa)).Should().Equal("SYN\\GMSA_IN");
        ActivityGroup handed = Group(report, ActivityGroupKeys.Handed);
        Accounts(handed).Should().BeEquivalentTo("SYN\\HAND", "SYN\\OWN_MOVE");
        handed.Items.Select(i => i.Teams).Should().OnlyContain(t => t == "SYN EKIP A → SYN EKIP B");
        Accounts(Group(report, ActivityGroupKeys.Changed)).Should().BeEquivalentTo("SYN\\DEL_V", "SYN\\OWN_FIRST", "SYN\\PWD", "SYN\\PLAN");
        report.Activity!.AccountsActed.Should().Be(8, "DEL_V is one account although it is both deleted and closed");
    }

    [Fact]
    public void ExcludesReviewVoidedUndatedAndAfterCutoffFromThePeriod()
    {
        ServiceAccountReport report = Report();

        string[] inPeriod = [.. report.Activity!.Groups.Where(g => g.Key != ActivityGroupKeys.Pending).SelectMany(g => g.Items).Select(i => i.Account)];
        inPeriod.Should().NotContain(["SYN\\REVIEW", "SYN\\VOID", "SYN\\NODATE", "SYN\\FUTURE", "SYN\\GMSA_OLD"]);
        Accounts(Group(report, ActivityGroupKeys.Pending)).Should().NotContain(["SYN\\VOID", "SYN\\FUTURE", "SYN\\REVIEW"]);
    }

    [Fact]
    public void Pending_ListsEveryUnverifiedReportAllTime_OldestFirst_UndatedLast()
    {
        ActivityGroup pending = Group(Report(), ActivityGroupKeys.Pending);

        Accounts(pending).Should().Equal("SYN\\GMSA_OLD", "SYN\\DEL_R", "SYN\\GMSA_IN", "SYN\\PWD", "SYN\\NODATE");
        pending.Items[0].Status.Should().Be("Bildirildi · doğrulanmadı · 15 gün");
        pending.Items[^1].Status.Should().Be("Bildirildi · doğrulanmadı · tarih bilinmiyor");
        pending.Items.Should().NotContain(i => i.Account == "SYN\\DEL_V", "a verified action is not pending");
    }

    [Fact]
    public void PeriodFilter_ChangesWhatIsListed_ButNotThePendingBacklog()
    {
        ServiceAccountReport lastTwoDays = Report(start: new DateOnly(2026, 10, 15), end: new DateOnly(2026, 10, 17));

        Accounts(Group(lastTwoDays, ActivityGroupKeys.Deleted)).Should().BeEmpty();
        Accounts(Group(lastTwoDays, ActivityGroupKeys.Changed)).Should().BeEquivalentTo("SYN\\PWD", "SYN\\PLAN");
        Group(lastTwoDays, ActivityGroupKeys.Pending).Lines.Should().Be(5);
        lastTwoDays.Activity!.Cards.Single(c => c.Label == "Doğrulanmayı bekleyen").Value.Should().Be(5);
    }

    [Fact]
    public void VerificationInThePeriod_CountsEvenWhenTheActionWasReportedEarlier()
    {
        DateOnly d(int day) => new(2026, 10, day);
        ReportFacts facts = Facts() with
        {
            Actions = [Action("DEL_V", ServiceAccountActionType.Deletion, ServiceAccountActionResult.Verified, d(1), d(15), verifier: true, or: true, closure: true)]
        };

        ActivityLine line = Group(Report(facts), ActivityGroupKeys.Deleted).Items.Single();

        line.Date.Should().Be(d(15));
        line.Status.Should().Be("Doğrulandı · 15.10.2026");
    }

    [Fact]
    public void VerifiedResultWithoutVerifier_IsStillNotVerified()
    {
        DateOnly d(int day) => new(2026, 10, day);
        ReportFacts facts = Facts() with { Actions = [Action("DEL_R", ServiceAccountActionType.Deletion, ServiceAccountActionResult.Verified, d(12), d(13), verifier: false)] };

        Group(Report(facts), ActivityGroupKeys.Deleted).Items.Single().Status.Should().Be("Bildirildi · doğrulanmadı");
        Group(Report(facts), ActivityGroupKeys.Pending).Lines.Should().Be(1);
    }

    [Fact]
    public void Cards_AreAtMostFive_EachWithOneSentence()
    {
        ReportCard[] cards = [.. Report().Activity!.Cards];

        cards.Should().HaveCount(5);
        cards.Select(c => c.Label).Should().Equal("Toplam hesap", "Bu dönem işlem gören", "Doğrulanmayı bekleyen", "Sahip ekibi olmayan", "gMSA ilerlemesi");
        cards.Should().OnlyContain(c => c.Sentence.EndsWith('.'));
        cards[0].Value.Should().Be(_accounts.Count);
        cards[1].Value.Should().Be(8);
    }

    [Fact]
    public void WithoutPlanTables_PlansAreNotCounted_AndTheNoteSaysSo()
    {
        ReportFacts facts = Facts(Inputs() with { Plans = [], PlansAvailable = false });

        ServiceAccountReport report = Report(facts);

        Accounts(Group(report, ActivityGroupKeys.Changed)).Should().NotContain("SYN\\PLAN");
        report.Activity!.Notes.Should().Contain(n => n.Contains("plan tamamlanmaları sayılmadı", StringComparison.Ordinal));
    }

    [Fact]
    public void AReportWithoutActivityInputs_HasNoActivityPage()
    {
        ServiceAccountReport report = ServiceAccountMetrics.Compute(Facts() with { Activity = null }, _start, _endExclusive, _asOf, "SYN", ReportPeriods.Custom);

        report.Activity.Should().BeNull();
        ReportDocument.From(report, Guid.NewGuid(), new string('a', 64), null, _created, "Sentetik").ActivityPage.Should().BeNull();
    }

    [Fact]
    public void LargeGroups_AreCapped_ButTheCountIncludesEveryLine()
    {
        List<AccountFact> many = [.. Enumerable.Range(0, ServiceAccountActivityRules.MaxLinesPerGroup + 25).Select(i => new AccountFact(Guid.NewGuid(), $"SYN\\BULK{i:D4}", _teamA, null))];
        ActionFact[] actions = [.. many.Select(a => new ActionFact(Guid.NewGuid(), a.Id, new ActionFacts(ServiceAccountActionType.Deletion, ServiceAccountActionResult.Performed,
            ServiceAccountRecordKind.Intermediate, new DateOnly(2026, 10, 12), null, false, true, false, false), TimePrecision.DateOnly, null, null))];
        ReportFacts facts = new(many, [], actions, [], [], [], [], new Dictionary<Guid, string>(), Activity: new ActivityFacts([], [], [], true));

        ActivityGroup deleted = Group(Report(facts), ActivityGroupKeys.Deleted);

        deleted.Lines.Should().Be(many.Count);
        deleted.Items.Should().HaveCount(ServiceAccountActivityRules.MaxLinesPerGroup);
        deleted.NotShown.Should().Be(25);
    }

    [Fact]
    public void ThePayload_CarriesNoPersonColumnOrId()
    {
        string json = JsonSerializer.Serialize(Report().Activity, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().NotContainEquivalentOf("person").And.NotContainEquivalentOf("user").And.NotContainEquivalentOf("kişi");
        typeof(ActivityLine).GetProperties().Select(p => p.Name).Should().Equal("Account", "Operation", "Date", "Teams", "Status", "Evidence");
    }

    [Fact]
    public void Document_PutsTheActivityPageFirst_AndSimplifiesTheSummaryToFiveCards()
    {
        var document = ReportDocument.From(Report(), Guid.NewGuid(), new string('a', 64), "SYN", _created, "Sentetik");

        document.ActivityPage!.Select(s => s.Title).Should().Equal(ReportDocument.ActivityTitle, "Silinen hesaplar", "Devredilen hesaplar (eski → yeni sorumlu ekip)",
            "gMSA'ya geçen hesaplar", "Değişen hesaplar", "Bildirilen ama doğrulanmayan işlemler (tüm zamanlar)");
        document.Dashboard!.Tiles.Should().HaveCount(5);
        document.Dashboard.Tiles.Should().OnlyContain(t => !string.IsNullOrEmpty(t.Note));
        document.Dashboard.Blocks.Should().BeEmpty("the long tables moved to the detail sections");
    }

    [Fact]
    public void Workbook_FirstSheetIsTheActivityPage_WithTheSameRowsAndAValidPackage()
    {
        var document = ReportDocument.From(Report(), Guid.NewGuid(), new string('a', 64), "SYN", _created, "Sentetik");
        byte[] bytes = ReportWorkbookWriter.Write(document);

        IReadOnlyList<SheetData> sheets = SpreadsheetReader.Read(bytes, new SpreadsheetLimits(), null, out _);

        sheets[0].Name.Should().Be(ReportDocument.ActivityTitle);
        sheets[1].Name.Should().Be("Yönetici özeti");
        string[] firstSheet = [.. sheets[0].Rows.SelectMany(r => r.Cells.Values).Select(c => c.Text ?? c.Number?.ToString() ?? string.Empty)];
        firstSheet.Should().Contain([ReportDocument.ActivityTitle, "Silinen hesaplar", "SYN\\DEL_V", "SYN\\DEL_R", "SYN EKIP A → SYN EKIP B", "Bildirildi · doğrulanmadı", "SYN\\GMSA_IN"]);
        string[] summary = [.. sheets[1].Rows.SelectMany(r => r.Cells.Values).Select(c => c.Text ?? string.Empty)];
        summary.Should().Contain(["Toplam hesap", "Doğrulanmayı bekleyen"]);
        using var package = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        new OpenXmlValidator(FileFormatVersions.Office2016).Validate(package).Should().BeEmpty();
    }

    [Fact]
    public void Pdf_FirstPageStartsWithTheActivityPage_BeforeTheSummaryAndDetails()
    {
        var document = ReportDocument.From(Report(), Guid.NewGuid(), new string('a', 64), "SYN", _created, "Sentetik");

        IReadOnlyList<string> lines = ReportPdfWriter.ExtractLines(ReportPdfWriter.Write(document));

        int activity = IndexOf(lines, "DÖNEM İÇİNDE YAPILAN İŞLEMLER");
        int deleted = IndexOf(lines, "SİLİNEN HESAPLAR");
        int summary = IndexOf(lines, "YÖNETİCİ ÖZETİ");
        activity.Should().BeGreaterThan(-1).And.BeLessThan(deleted);
        deleted.Should().BeLessThan(summary);
        summary.Should().BeLessThan(IndexOf(lines, "ÖZET"));
        string text = string.Join('\n', lines);
        text.Should().Contain("SYN\\DEL_V").And.Contain("SYN EKIP A → SYN EKIP B").And.Contain("Bildirildi · doğrulanmadı").And.Contain("Doğrulanmayı bekleyen");
        lines.Should().NotContain(l => l.Contains('?', StringComparison.Ordinal) && l.Contains("→", StringComparison.Ordinal), "the arrow glyph must render, not fall back to a question mark");
    }

    private static int IndexOf(IReadOnlyList<string> lines, string heading)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith(heading, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
