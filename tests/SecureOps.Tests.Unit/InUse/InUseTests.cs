using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Commands;
using SecureOps.Infrastructure.InUse;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Unit.InUse;

public sealed partial class InUseTests
{
    [Theory]
    [InlineData("Complete")]
    [InlineData("Observed")]
    public async Task Archive_RequiresCompleteAnswers_PreservesEvidenceAndActor_AndReplaysOriginalBytes(string relationship)
    {
        string directory = Path.Combine(Path.GetTempPath(), "inuse-archive-" + Guid.NewGuid());
        var archive = new InUseReportArchive(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["InUseReports:Directory"] = directory }).Build());
        var f = new Fixture(archive: archive);
        InUseRecord record = await f.ImportAsync();
        f.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(new InUseBatch([record.Source with { ServiceItemsState = relationship }], false));
        await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token);
        record = (await f.Repository.GetAsync(record.Id, _token))!;
        record = (await f.Service.AssignAsync(_principal, _context, record.Id, new(record.Version, f.User.Id, "Manual"), _token)).Value!;
        record = (await f.Service.SaveDraftAsync(_principal, _context, record.Id, new(record.Version, record.SourceVersion,
            [new(record.Source.Servers[0].Id, "InternetOut", "Yes", "Historical evidence")], "Historical note"), _token)).Value!;
        InUseResult<InUseReport> missing = await f.Service.ExportAsync(_principal, _context, record.Id, new(record.Version, true), _token);
        missing.Error.Should().Be("InUseIncomplete");
        missing.Detail.Should().Contain(record.Source.Servers[0].Id).And.Contain("İnternetten sunucuya erişim");
        archive.Versions(record.Id).Should().BeEmpty();
        InUseAnswer[] answers = record.Source.Servers.SelectMany(s => InUseChecks.OperatorCodes.Select(c => new InUseAnswer(s.Id, c, "No", ""))).ToArray();
        record = (await f.Service.SaveDraftAsync(_principal, _context, record.Id, new(record.Version, record.SourceVersion, answers, ""), _token)).Value!;
        record.Draft!.Notes.Should().Be("Historical note");
        record.Draft.Answers.Should().Contain(a => a.Evidence == "Historical evidence" && a.Value == "No");
        f.Audit.WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>()).Returns(_ => throw new IOException("Synthetic audit failure"));
        (await f.Service.ExportAsync(_principal, _context, record.Id, new(record.Version, true), _token)).Error.Should().Be("PersistenceUnavailable");
        archive.Versions(record.Id).Should().BeEmpty();
        f.Audit.WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        InUseReport first = (await f.Service.ExportAsync(_principal, _context, record.Id, new(record.Version, true), _token)).Value!;
        InUseReport replay = (await f.Service.ExportAsync(_principal, _context, record.Id, new(record.Version, true), _token)).Value!;
        first.Archived.Should().BeTrue();
        first.PreparedBy.Should().Be(f.User.Id);
        first.SourceId.Should().Be(record.Source.Id);
        replay.Should().BeEquivalentTo(first);
        archive.Versions(record.Id).Should().Equal(record.Version);
        await f.Audit.Received().WriteAsync(Arg.Is<AuditEvent>(e => e.Actor == f.User.Id.ToString("D") && e.Action == "InUseReportArchiveAuthorized"), Arg.Any<CancellationToken>());
        long archivedVersion = record.Version;
        record = (await f.Service.SaveDraftAsync(_principal, _context, record.Id, new(record.Version, record.SourceVersion, answers, ""), _token)).Value!;
        (await f.Service.ExportAsync(_principal, _context, record.Id, new(record.Version, ArchivedVersion: archivedVersion), _token)).Value!.Sha256.Should().Be(first.Sha256);
        await f.Source.DidNotReceive().DiagnoseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("deployment")]
    [InlineData("file")]
    [InlineData("pending")]
    public async Task Archive_UnsafeOrFailedStorage_NeverReportsSuccess(string failure)
    {
        string directory = failure == "deployment" ? Path.Combine(AppContext.BaseDirectory, "reports")
            : Path.Combine(Path.GetTempPath(), "inuse-failure-" + Guid.NewGuid());
        var archive = new InUseReportArchive(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["InUseReports:Directory"] = directory }).Build());
        var f = new Fixture(archive: archive);
        InUseRecord record = await f.ImportAsync();
        record = record with { Draft = new(record.SourceVersion, [], "", f.User.Id, DateTimeOffset.UtcNow) };
        InUseReport report = InUseWorkbook.Create(record, f.User.Id, DateTimeOffset.UtcNow);
        if (failure == "file")
        { await File.WriteAllTextAsync(directory, "synthetic obstruction"); }
        if (failure == "pending")
        { Directory.CreateDirectory(Path.Combine(directory, record.Id.ToString("D"), record.Version + ".json.pending")); }
        Func<Task> write = async () => await archive.AccessAsync(record.Id, record.Version, report, _ => Task.FromResult(true), _token);
        await write.Should().ThrowAsync<Exception>();
        File.Exists(Path.Combine(directory, record.Id.ToString("D"), record.Version + ".json")).Should().BeFalse();
    }

    private static readonly CancellationToken _token = CancellationToken.None;

    [Fact]
    public async Task SemanticServers_PersistExportAndInvalidateWithoutLosingDraftOrReviewer()
    {
        var f = new Fixture();
        using var document = JsonDocument.Parse(InUseServiceItemParserTests.Response(4));
        InUseSource source = (await new LocalInUseSourceClient().DiscoverAsync(_token)).Records[0] with
        { Servers = InUseServiceItemParser.Parse(document.RootElement), ServiceItemsState = "Observed", RelationshipEvidence = "Observed; completeness unverified" };
        f.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(new InUseBatch([source], false, "SourceCompletenessUnverified"));
        await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token);
        InUseRecord record = (await f.Repository.QueryAsync(new(), f.User.Id, _token)).Items.Single();
        record = (await f.Service.AssignAsync(_principal, _context, record.Id, new(record.Version, f.User.Id, "Manual"), _token)).Value!;
        InUseAnswer[] answers = source.Servers.SelectMany(s => InUseChecks.OperatorCodes.Select(c => new InUseAnswer(s.Id, c, "No", ""))).ToArray();
        record = (await f.Service.SaveDraftAsync(_principal, _context, record.Id, new(record.Version, record.SourceVersion, answers, ""), _token)).Value!;
        InUseReport report = (await f.Service.ExportAsync(_principal, _context, record.Id, new(record.Version), _token)).Value!;
        report.Sheets[0].Rows.Single(r => r[0] == "HOSTNAME").Skip(1).Should().Equal(Enumerable.Range(0, 4).Select(i => "synthetic-server-" + i));
        report.Sheets[0].Rows.Single(r => r[0] == "SI_ENVIRONMENT").Skip(1).Should().Equal(Enumerable.Range(0, 4).Select(i => $"display-{i}-p_SI_def_environment"));
        report.Sheets[3].Rows.Skip(1).Select(r => r[3]).Should().Equal("2000", "2001", "2002", "2003");
        report.Sheets[4].Rows.Should().Contain(r => r.Contains("Observed"));
        // A shrinking bounded result is not authoritative deletion, and prevents current archival.
        f.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(new InUseBatch([source with { Servers = source.Servers.Take(3).ToArray() }], false));
        await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token);
        InUseRecord retained = (await f.Repository.GetAsync(record.Id, _token))!;
        retained.Source.ServiceItemsState.Should().Be("Partial");
        retained.Source.Servers.Should().HaveCount(4);
        retained.Draft.Should().BeEquivalentTo(record.Draft);
        retained.AssigneeId.Should().Be(f.User.Id);
        retained.Status.Should().Be("Stale");
        (await f.Service.ExportAsync(_principal, _context, record.Id, new(retained.Version), _token)).Error.Should().Be("InUseConflict");
        retained = (await f.Service.SaveDraftAsync(_principal, _context, record.Id, new(retained.Version, retained.SourceVersion, answers, ""), _token)).Value!;
        (await f.Service.ExportAsync(_principal, _context, record.Id, new(retained.Version, true), _token)).Error.Should().Be("InUseIncomplete");
        var incomplete = source.Servers[0].Fields.ToDictionary(p => p.Key, p => p.Value);
        incomplete["SI_ENVIRONMENT"] = new(null, "Missing response cell: KEY.(LCSIMS_ServiceInstance)m_rid.p_SI_def_environment");
        f.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(new InUseBatch([source with
        { Servers = source.Servers.Select((s, i) => i == 0 ? s with { Fields = incomplete } : s).ToArray() }], false));
        await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token);
        retained = (await f.Repository.GetAsync(record.Id, _token))!;
        retained.Source.ServiceItemsState.Should().Be("Partial");
        retained.Source.Servers[0].Fields["SI_ENVIRONMENT"].Value.Should().Be(source.Servers[0].Fields["SI_ENVIRONMENT"].Value);
        retained.Source.Servers[0].Fields["SI_ENVIRONMENT"].Source.Should().StartWith("Prior value retained;");
    }
    private static readonly ClaimsPrincipal _principal = new();
    private static readonly AccessOperationContext _context = new("synthetic", "inuse-test", null);

    [Fact]
    public async Task Diagnostic_AuditFailureAndMissingCapability_PreventSourceRead()
    {
        foreach (string role in new[] { "Admin", "InUseCoordinator" })
        {
            var f = new Fixture(role);
            InUseRecord record = await f.ImportAsync();
            f.Audit.WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>()).Returns(_ => throw new IOException("Synthetic audit failure"));
            (await f.Service.DiagnoseAsync(_principal, _context, record.Id, new(record.Version, record.Source.Id), _token)).Error
                .Should().Be(role == "Admin" ? "PersistenceUnavailable" : "AccessDenied");
            await f.Source.DidNotReceive().DiagnoseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task RelationshipStates_PreserveEvidenceAndAssignment_ButInvalidateReview()
    {
        var f = new Fixture();
        InUseRecord record = await f.ImportAsync();
        InUseSource four = record.Source with
        {
            Servers = Enumerable.Range(1, 4).Select(i => new InUseServer($"item-{i}",
            new Dictionary<string, InUseEvidence>
            {
                ["Virtual PC User"] = new(i == 1 ? null : $"user-{i}", "Synthetic label only"),
                ["RFC Kaydı"] = new("OR-OTHER", "Synthetic related request")
            })).ToArray(),
            ServiceItemsState = "Complete",
            AffectedAssetsState = "Complete",
            AffectedAssetCount = 0
        };
        f.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(new InUseBatch([four], true));
        await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token);
        record = (await f.Repository.GetAsync(record.Id, _token))!;
        record = (await f.Service.AssignAsync(_principal, _context, record.Id, new(record.Version, f.User.Id, "Manual"), _token)).Value!;
        record = (await f.Service.SaveDraftAsync(_principal, _context, record.Id, new(record.Version, record.SourceVersion, [], "Unknown"), _token)).Value!;
        record.Source.Requester.Should().NotBe(record.Source.Creator);
        foreach (string state in new[] { "Failed", "Forbidden", "Ambiguous", "NotQueried" })
        {
            f.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(new InUseBatch([four with { Servers = [], ServiceItemsState = state }], false));
            await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token);
            InUseRecord retained = (await f.Repository.GetAsync(record.Id, _token))!;
            retained.Source.Servers.Should().HaveCount(4);
            retained.Source.AffectedAssetCount.Should().Be(0);
            retained.AssigneeId.Should().Be(f.User.Id);
            retained.Status.Should().Be("Stale");
            (await f.Service.ExportAsync(_principal, _context, record.Id, new(retained.Version), _token)).Error.Should().Be("InUseConflict");
        }
    }

    [Fact]
    public async Task Refresh_FailureAndPartialAndEmpty_RetainDataAndSuccessfulTimestamp()
    {
        var fixture = new Fixture();
        InUseRecord record = await fixture.ImportAsync();
        InUseRefreshState state = await fixture.Repository.StateAsync(_token);
        fixture.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns<InUseBatch>(_ => throw new IOException("synthetic"));
        (await fixture.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token)).Value!.Issue.Should().Be("SourceUnavailableOrMalformed");
        fixture.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns<InUseBatch>(_ => throw new InvalidDataException("synthetic"));
        (await fixture.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token)).Value!.Issue.Should().Be("SourceUnavailableOrMalformed");
        (await fixture.Repository.StateAsync(_token)).LastSuccessfulAt.Should().Be(state.LastSuccessfulAt);
        (await fixture.Repository.GetAsync(record.Id, _token)).Should().BeEquivalentTo(record);
        fixture.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(new InUseBatch([], false, "Partial"));
        await fixture.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token);
        (await fixture.Repository.QueryAsync(new(), fixture.User.Id, _token)).Total.Should().Be(2);
        fixture.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(new InUseBatch([], true));
        await fixture.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token);
        (await fixture.Repository.QueryAsync(new(), fixture.User.Id, _token)).Total.Should().Be(2);
    }

    [Fact]
    public async Task AssignmentAndDraft_ExactVersionsAndStableIdentities_AreRequired()
    {
        var f = new Fixture();
        InUseRecord record = await f.ImportAsync();
        record.Source.ServiceOwner.Value.Should().BeNull();
        record = (await f.Service.SaveDraftAsync(_principal, _context, record.Id, new(record.Version, record.SourceVersion, [], ""), _token)).Value!;
        record.AssigneeId.Should().BeNull();
        record.Draft!.ReviewedBy.Should().Be(f.User.Id);
        (await f.Service.AssignAsync(_principal, _context, record.Id, new(record.Version, Guid.NewGuid(), "Unknown exact user"), _token))
            .Error.Should().Be("InUseAssigneeUnavailable");
        InUseResult<InUseRecord>[] assignments = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            f.Service.AssignAsync(_principal, _context, record.Id, new(record.Version, f.User.Id, "Explicit review assignment"), _token)));
        assignments.Count(r => r.Error is null).Should().Be(1);
        assignments.Count(r => r.Error == "InUseConflict").Should().Be(3);
        record = assignments.Single(r => r.Error is null).Value!;
        var request = new SaveInUseDraftRequest(record.Version, record.SourceVersion,
            [new(record.Source.Servers[0].Id, "InternetOut", "Yes", "Synthetic check"), new(record.Source.Servers[1].Id, "InternetOut", "No", "Separate synthetic check")], "Reviewed individually");
        InUseResult<InUseRecord>[] saves = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => f.Service.SaveDraftAsync(_principal, _context, record.Id, request, _token)));
        saves.Count(r => r.Error is null).Should().Be(1);
        record = saves.Single(r => r.Error is null).Value!;
        record.Draft!.Answers.Select(a => a.Value).Should().Equal("Yes", "No");
        (await f.Service.ExportAsync(_principal, _context, record.Id, new(request.ExpectedVersion), _token)).Error.Should().Be("InUseConflict");
        InUseReport report = (await f.Service.ExportAsync(_principal, _context, record.Id, new(record.Version), _token)).Value!;
        report.Sheets[0].Rows[14].Should().Equal("Sunucudan İnternete Erişim Var mı ?", "Evet", "Hayır");
        InUseSource changed = record.Source with { Title = "Changed source evidence" };
        f.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(new InUseBatch([changed], true));
        await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token);
        InUseRecord stale = (await f.Repository.GetAsync(record.Id, _token))!;
        stale.Status.Should().Be("Stale");
        stale.Draft.Should().BeEquivalentTo(record.Draft);
        (await f.Service.ExportAsync(_principal, _context, stale.Id, new(stale.Version), _token)).Error.Should().Be("InUseConflict");
    }

    [Fact]
    public async Task AuditFailure_RollsBackRefreshAssignmentDraftAndExport()
    {
        var f = new Fixture();
        InUseRecord record = await f.ImportAsync();
        f.Audit.WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>()).Returns(_ => throw new IOException("Synthetic audit failure"));
        (await f.Service.AssignAsync(_principal, _context, record.Id, new(record.Version, f.User.Id, "must roll back"), _token)).Error.Should().Be("PersistenceUnavailable");
        (await f.Repository.GetAsync(record.Id, _token)).Should().BeEquivalentTo(record);
        InUseRefreshState state = await f.Repository.StateAsync(_token);
        (await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token)).Error.Should().Be("PersistenceUnavailable");
        (await f.Repository.StateAsync(_token)).Should().BeEquivalentTo(state);
    }

    [Theory]
    [InlineData("Operator")]
    [InlineData("Lead")]
    [InlineData("JiraPublisher")]
    [InlineData("ResourceCurator")]
    public async Task ExistingRoles_DoNotImplicitlyAcquireInUseOrChangeResources(string role)
    {
        var f = new Fixture(role);
        (await f.Service.QueryAsync(_principal, _context, new(), _token)).Error.Should().Be("AccessDenied");
        (await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token)).Error.Should().Be("AccessDenied");
        await f.Source.DidNotReceiveWithAnyArgs().DiscoverAsync(default);
        AccessRoleCatalog.GetCapabilities(["InUseCoordinator"]).Should().NotContain(Capabilities.OperationalRecordsCreateJira)
            .And.NotContain(Capabilities.ResourcesManage);
    }

    [Fact]
    public async Task Workbook_PreservesLegacyLayoutAndUnknowns_TextOnlyAndProvenance()
    {
        var f = new Fixture();
        InUseRecord record = await f.ImportAsync();
        InUseServer server = record.Source.Servers[0] with
        { Fields = new Dictionary<string, InUseEvidence> { ["HOSTNAME"] = new(" =HYPERLINK(\"unsafe\")", "Synthetic") } };
        record = record with
        {
            Source = record.Source with { Servers = [server] },
            Draft = new(record.SourceVersion, [], "@unsafe", f.User.Id, DateTimeOffset.UtcNow)
        };
        InUseReport report = InUseWorkbook.Create(record, f.User.Id, DateTimeOffset.UtcNow);
        report.Sheets.Select(s => s.Name).Should().Equal("Sunucular", "CheckList_TEKNIK", "CheckList_THY", "NMS", "Provenance", "ReviewEvidence");
        report.Sheets[0].Rows.Should().HaveCount(29);
        report.Sheets[3].Rows[0].Should().HaveCount(22);
        report.Sheets[0].Rows[2][1].Should().StartWith("'");
        report.Sheets[3].Rows[1][0].Should().Be("Unknown / not verified");
        report.Sheets[1].Rows.Should().BeEmpty();
        report.Sheets[4].Rows.Should().Contain(r => r.Contains(record.SourceHash));
        report.Sheets[4].Rows.Should().Contain(r => r[0] == "Relationships" && r[1] == record.Source.RelationshipEvidence);
        using ZipArchive zip = new(new MemoryStream(report.Content));
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            using Stream stream = entry.Open();
            var xml = XDocument.Load(stream);
            if (entry.FullName.EndsWith(".rels", StringComparison.Ordinal))
            {
                xml.Root!.Name.Should().Be(XName.Get("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships"));
            }
            if (entry.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal))
            {
                xml.Root!.Name.Should().Be(XName.Get("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main"));
                xml.Descendants().Where(e => e.Name.LocalName == "c").Should().NotContain(e => (string?)e.Attribute("t") != "inlineStr");
            }
            xml.Descendants().Should().NotContain(e => e.Name.LocalName == "f");
            xml.Descendants().Attributes().Should().NotContain(a => a.Name.LocalName == "TargetMode" && a.Value == "External");
        }
        report.Sha256.Should().HaveLength(64);
    }

    [Fact]
    public async Task InvalidOrDuplicateBatch_DoesNotReplacePersistedEvidence()
    {
        var f = new Fixture();
        InUseRecord record = await f.ImportAsync();
        f.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(new InUseBatch([record.Source, record.Source], false));
        (await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token)).Value!.Issue.Should().Be("SourceUnavailableOrMalformed");
        (await f.Repository.GetAsync(record.Id, _token)).Should().BeEquivalentTo(record);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewCapability_DoesNotRequireAssignment_AndNeverChangesReviewer(bool assignedElsewhere)
    {
        var f = new Fixture("InUseReviewer");
        InUseBatch batch = await new LocalInUseSourceClient().DiscoverAsync(_token);
        await f.Repository.RefreshAsync(0, batch, null, new AuditEvent { Actor = "synthetic", Action = "Seed" }, _token);
        InUseRecord record = (await f.Repository.QueryAsync(new(), f.User.Id, _token)).Items[0];
        if (assignedElsewhere)
        {
            await f.Repository.SaveAsync(record with { Version = record.Version + 1, AssigneeId = Guid.NewGuid(), AssigneeLabel = "synthetic:other" },
                record.Version, new AuditEvent { Actor = "synthetic", Action = "Seed" }, _token);
            record = (await f.Repository.GetAsync(record.Id, _token))!;
        }
        var request = new SaveInUseDraftRequest(record.Version, record.SourceVersion, [], "");
        InUseResult<InUseRecord>[] results = await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => f.Service.SaveDraftAsync(_principal, _context, record.Id, request, _token)));
        results.Count(r => r.Error is null).Should().Be(1);
        results.Count(r => r.Error == "InUseConflict").Should().Be(2);
        InUseRecord saved = results.Single(r => r.Error is null).Value!;
        saved.AssigneeId.Should().Be(record.AssigneeId);
        saved.Draft!.ReviewedBy.Should().Be(f.User.Id);
        (await f.Service.ExportAsync(_principal, _context, record.Id, new(saved.Version), _token)).Value!.PreparedBy.Should().Be(f.User.Id);
        (await f.Service.AssignAsync(_principal, _context, record.Id, new(saved.Version, null, "Not authorized"), _token)).Error.Should().Be("AccessDenied");
        await f.Source.DidNotReceiveWithAnyArgs().DiscoverAsync(default);
    }

    [Fact]
    public async Task Refresh_DifferentCommandsShareScope_ReleaseAfterFailureAndKeepReadsAvailable()
    {
        var f = new Fixture();
        InUseRecord record = await f.ImportAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<InUseBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Source.ClearReceivedCalls();
        f.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(_ => { entered.SetResult(); return finish.Task; });
        Task<InUseResult<InUseRefreshState>> first = f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token);
        await entered.Task;
        try
        {
            (await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token)).Error.Should().Be("InUseConflict");
            (await f.Service.GetAsync(_principal, _context, record.Id, _token)).Value.Should().NotBeNull();
            await f.Source.Received(1).DiscoverAsync(Arg.Any<CancellationToken>());
        }
        finally { finish.SetException(new IOException("Synthetic source outage")); }
        (await first).Value!.Issue.Should().Be("SourceUnavailableOrMalformed");
        f.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(new InUseBatch([], false));
        (await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token)).Error.Should().BeNull();
        (await f.Repository.GetAsync(record.Id, _token)).Should().BeEquivalentTo(record);
    }

    private sealed class Fixture
    {
        public IAuditWriter Audit { get; } = Substitute.For<IAuditWriter>();
        public IInUseSourceClient Source { get; } = Substitute.For<IInUseSourceClient>();
        public InMemoryInUseRepository Repository { get; }
        public InUseService Service { get; }
        public ApplicationUser User { get; }
        public Fixture(string role = "Admin", InUseReportArchive? archive = null)
        {
            Repository = new(Audit);
            User = new(Guid.NewGuid(), "synthetic:reviewer", "test", AccessStatus.Approved, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, 1, [role], AccessRoleCatalog.GetCapabilities([role]));
            IApplicationAccessService access = Substitute.For<IApplicationAccessService>();
            access.GetCurrentAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<AccessOperationContext>(), Arg.Any<CancellationToken>())
                .Returns(AccessServiceResult<EnsureAccessUserResult>.Success(new(User, null, false, false)));
            IAccessRepository users = Substitute.For<IAccessRepository>();
            users.GetUserAsync(User.Id, Arg.Any<CancellationToken>()).Returns(User);
            Service = new(Repository, Source, access, users, new InMemoryCommandIdempotencyStore(TimeProvider.System), NullLogger<InUseService>.Instance, archive);
        }
        public async Task<InUseRecord> ImportAsync()
        {
            Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(await new LocalInUseSourceClient().DiscoverAsync(_token));
            (await Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token)).Error.Should().BeNull();
            return (await Repository.QueryAsync(new(), User.Id, _token)).Items[0];
        }
    }
}
