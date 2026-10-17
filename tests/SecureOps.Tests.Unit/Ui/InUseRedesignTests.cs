using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using SecureOps.Shared.Contracts.InUse;
using SecureOps.Ui.Services;
using SecureOps.Ui.Shared.Components;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// In Use list redesign (IU-UI): one badge and one next step per row, honest wording for finished records, the closed/completed page
/// reads only what the server returns, and the guide's tour points at areas the pages really render. Synthetic records only.
/// </summary>
public sealed partial class InUseRedesignTests
{
    private static InUseRecord Record(int servers = 2, bool answered = false, InUseSource? source = null) =>
        new(Guid.NewGuid(), source ?? Source(servers), "hash", 5, 4, null, null,
            answered ? Draft(servers) : null, DateTimeOffset.UtcNow)
        { ReviewSourceVersion = 5 };

    private static InUseSource Source(int servers) => new("1", "OR-SYN-1001", "Synthetic title", new("Synthetic requester", "Synthetic"),
        new(null, "Synthetic"), new(null, "Synthetic"),
        [.. Enumerable.Range(1, servers).Select(i => new InUseServer($"{i}", new Dictionary<string, InUseEvidence>
        {
            ["HOSTNAME"] = new($"syn-{i}", "Synthetic")
        }))], "Synthetic", true)
    { ServiceItemsState = "Complete", AffectedAssetsState = "Complete", AffectedAssetCount = 0 };

    private static InUseDraft Draft(int servers) => new(5, [.. Enumerable.Range(1, servers).SelectMany(i => InUseChecks.OperatorCodes.Select(c => new InUseAnswer($"{i}", c, "Yes", "")))],
        "", Guid.NewGuid(), DateTimeOffset.UtcNow)
    { ReviewedByLabel = "Synthetic Operator", ReviewedByAccount = "syn.operator" };

    [Fact]
    public void RowBadge_NamesTheMostUrgentTrueFact_AndNeverClaimsMore()
    {
        InUsePresentation.RowBadge(Record()).Label.Should().Be("Cevaplar eksik");
        InUsePresentation.RowBadge(Record(0)).Should().Be(new InUseBadge("Sunucu ilişkisi yok", InUseBadgeTone.Caution));
        InUsePresentation.RowBadge(Record() with { SourceObservationMissing = true }).Label.Should().Be("Son yenilemede görülmedi");
        InUsePresentation.RowBadge(Record() with { HasActiveExecution = true }).Tone.Should().Be(InUseBadgeTone.Caution);
        InUsePresentation.RowBadge(Record() with { Discarded = true }).Label.Should().Be("Taslak kaldırıldı");
        InUsePresentation.RowBadge(Record(answered: true)).Label.Should().Be("İnceleme kaydedildi")
            .And.NotContain("tamam", "a saved local review is not a completed request");
        InUsePresentation.NextStep(Record(answered: true)).Should().Be("Excel önizlemesini inceleyin");
        InUsePresentation.NextStep(Record()).Should().Be("Eksik cevapları tamamlayın");
    }

    [Fact]
    public void Closed_SeparatesTheKindsOfEvidence_AndNeverInventsAnApprover()
    {
        InUseRecord verified = Record(answered: true) with { ActivityVerifiedAt = DateTimeOffset.UtcNow };
        InUseClosedFacts facts = InUsePresentation.Closed(verified);
        facts.Kind.Should().Be("WASAS aktivitesi onaylandı (ürün kaydında doğrulandı)");
        facts.Meaning.Should().Contain("aktivite durumu okunmadı").And.NotContain("OR kapandı");
        facts.Approver.Should().Contain("bu listede yok");
        facts.Reviewer.Should().Contain("Synthetic Operator");
        facts.Report.Should().Contain("bu listede yok", "no completion intent means the report is unknown here, not absent");
        facts.Intent.Should().Be("Bu kayıtta tamamlama niyeti kaydı yok");

        InUseSource closed = Source(1) with { Lifecycle = new("Closed", "Synthetic source read") };
        InUsePresentation.Closed(Record(source: closed)).Kind.Should().Be("Genel OR kapalı");
        InUsePresentation.Closed(Record(source: closed)).Meaning.Should().Contain("WASAS aktivitesinin kendi durumu doğrulanmadı");

        InUseSource activity = Source(1) with { WasasActivity = new("Completed", "Synthetic source read") };
        InUsePresentation.Closed(Record(source: activity)).Kind.Should().Be("WASAS aktivitesi onaylandı (kaynakta doğrulandı)");

        InUseSource left = Source(1) with { CurrentStage = new(InUseActivity.NotAssignedToWasas, "Synthetic source read") };
        InUsePresentation.Closed(Record(source: left)).Kind.Should().Be("Kaynakta WASAS'a atanmış değil");
        InUsePresentation.Closed(Record(source: left)).Meaning.Should().Contain("naylandı mı geri mi gönderildi doğrulanmadı").And.Contain("tek başına onay kanıtı değildir");
        InUsePresentation.Closed(Record(source: left with { WasasActivity = new("NotApproved", "Synthetic source read") })).Kind.Should().Be("Onaylanmadı (ret/geri gönderme)");
        Record(source: left).TrackingOnly.Should().BeTrue("the server rule: left the team leaves the main list");
        Record(source: Source(1) with { CurrentStage = new("Unknown", "Synthetic source read") }).TrackingOnly.Should().BeFalse("unreadable stays listed");

        InUseClosedFacts unexplained = InUsePresentation.Closed(Record());
        unexplained.Kind.Should().Be("Durum doğrulanmadı");
        unexplained.Tone.Should().Be(InUseBadgeTone.Neutral);

        InUseRecord withIntent = verified with { Completion = new(Guid.NewGuid(), Guid.NewGuid(), 7, 5, "sha", DateTimeOffset.UtcNow, "VerificationPending") };
        InUsePresentation.Closed(withIntent).Report.Should().Be("Rapor sürümü 7");
        InUsePresentation.Closed(withIntent).Intent.Should().Contain("kaynaktan doğrulanmadı");
    }

    [Fact]
    public void ManualOrLocalEvidence_DoesNotMoveARecordToTheClosedList()
    {
        // The server's own tracking rule: only authoritative activity evidence or a source-closed OR. A local draft, report or manual note is not.
        Record(answered: true).TrackingOnly.Should().BeFalse();
        (Record(answered: true) with { Completion = new(Guid.NewGuid(), Guid.NewGuid(), 1, 5, "sha", DateTimeOffset.UtcNow, "ClosedVerified") }).TrackingOnly.Should().BeFalse();
        (Record() with { ActivityVerifiedAt = DateTimeOffset.UtcNow }).TrackingOnly.Should().BeTrue();
    }

    [Fact]
    public async Task Row_ShowsOneBadgeOneNextStepAndKeepsDetailInsideDisclosure()
    {
        string html = await RenderAsync(Record() with { SourceObservationMissing = true }, first: true);
        html.Should().Contain("OR-SYN-1001").And.Contain("Son yenilemede görülmedi").And.Contain("Sıradaki adım")
            .And.Contain("data-tour=\"inuse-next\"").And.Contain("<details").And.Contain("RFC talep eden · Ad soyad");
        Regex.Matches(html, "so-badge ").Count.Should().Be(1, "a row carries one status badge");
        html.Should().NotContain("so-badge--critical").And.NotContain("color:red");
        (await RenderAsync(Record(), first: false)).Should().NotContain("data-tour=");
    }

    [Fact]
    public void Guide_SaysWhatTheServerReportsAboutTheCompletionGate()
    {
        InUseTours.Flow(null)[3].Watch.Should().Contain("izin veriyor");
        InUseTours.Flow("")[3].Watch.Should().Contain("Kayıt sayfasında").And.NotContain("izin veriyor");
        InUseTours.Flow("Kaynağa yazma bayrağı kapalı.")[3].Watch.Should().Contain("Şu an kapalı").And.Contain("Kaynağa yazma bayrağı kapalı.");
        InUseTours.Flow(null).Should().HaveCount(4).And.OnlyContain(s => s.Do.Length > 20 && s.Watch.Length > 20);
        InUseTours.Record(true, true, "gerekçe").Single(s => s.Target == "inuse-complete").What.Should().Contain("Şu an kapalı: gerekçe");
    }

    [Fact]
    public void TourSteps_FollowPermissions()
    {
        new GuidedTourState(InUseTours.List(false)).Steps.Select(s => s.Target).Should().NotContain("inuse-refresh");
        new GuidedTourState(InUseTours.List(true)).Steps.Select(s => s.Target).Should().Contain("inuse-refresh");
        new GuidedTourState(InUseTours.Record(false, false, null)).Steps.Select(s => s.Target).Should()
            .BeEquivalentTo(["inuse-summary", "inuse-servers"], "a viewer may neither answer, save, report nor complete");
    }

    [Fact]
    public void EveryTourTarget_IsRenderedByItsPage()
    {
        string list = Ui("Pages", "InUse.razor") + Ui("Shared", "Components", "InUseRecordRow.razor") + Ui("Shared", "Components", "InUseCountStrip.razor");
        HashSet<string> rendered = [.. TourAttribute().Matches(list).Select(m => m.Groups["name"].Value)];
        foreach (string target in InUseTours.List(true).Concat(InUseTours.Record(true, true, null)).Select(s => s.Target))
        {
            rendered.Should().Contain(target);
        }
    }

    [Fact]
    public void Pages_UseTheServerListForClosedRecords_AndStoreNothingInTheBrowser()
    {
        string closed = Ui("Pages", "InUseClosed.razor");
        closed.Should().Contain("@page \"/in-use/closed\"").And.Contain("view=tracking").And.Contain("InUsePresentation.Closed");
        Ui("Pages", "InUse.razor").Should().Contain("RecheckUnchecked").And.Contain("listede bırakıldı");
        closed.Should().Contain("RecheckUnchecked").And.Contain("Ekibimizden çıkan");
        string code = Ui("Pages", "InUse.razor.cs");
        code.Should().Contain("_view = \"review\"", "finished records leave the main list by default").And.Contain("/in-use/closed");
        Ui("Pages", "InUse.razor").Should().NotContain("ResourceHelp").And.Contain("<InUseGuide");
        foreach (string source in new[] { closed, Ui("Shared", "Components", "InUseGuide.razor"), Ui("Services", "InUseTours.cs"), Ui("Services", "InUsePresentation.cs") })
        {
            source.Should().NotContain("localStorage").And.NotContain("sessionStorage");
        }
    }

    [Fact]
    public void RefreshAndTourButtons_AreNotBrandRed()
    {
        // Brand red is chrome-only (UI README): the list's filled action uses the info token and the tour start button is re-coloured.
        Ui("Pages", "InUse.razor").Should().Contain("Color=\"Color.Info\">In Use kaynağını yenile").And.NotContain("Color.Primary\">In Use kaynağını yenile");
        Ui("Shared", "Components", "InUseGuide.razor.css").Should().Contain(".so-tour-start { border-color: var(--so-info); color: var(--so-info); }");
        Ui("Pages", "InUse.razor.css").Should().Contain(".so-dark .so-inuse-primary");
    }

    private static async Task<string> RenderAsync(InUseRecord record, bool first)
    {
        ServiceCollection collection = new();
        collection.AddLogging().AddMudServices();
        await using ServiceProvider services = collection.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<InUseRecordRow>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(InUseRecordRow.Record)] = record,
                [nameof(InUseRecordRow.Href)] = "/in-use/x",
                [nameof(InUseRecordRow.First)] = first
            }))).ToHtmlString()));
    }

    private static string Ui(params string[] path) => File.ReadAllText(Path.Combine([Root(), "src", "SecureOps.Ui", .. path]));

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex("(?:data-tour=\"|[?] \")(?<name>inuse-[a-z-]+)\"")]
    private static partial Regex TourAttribute();
}
