using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Commands;
using SecureOps.Infrastructure.InUse;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Unit.InUse;

public sealed class InUseTests
{
    private static readonly CancellationToken _token = CancellationToken.None;
    private static readonly ClaimsPrincipal _principal = new();
    private static readonly AccessOperationContext _context = new("synthetic", "inuse-test", null);

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
        (await f.Service.SaveDraftAsync(_principal, _context, record.Id, new(record.Version, record.SourceVersion, [], ""), _token))
            .Error.Should().Be("InUseAssignmentRequired");
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

    private sealed class Fixture
    {
        public IAuditWriter Audit { get; } = Substitute.For<IAuditWriter>();
        public IInUseSourceClient Source { get; } = Substitute.For<IInUseSourceClient>();
        public InMemoryInUseRepository Repository { get; }
        public InUseService Service { get; }
        public ApplicationUser User { get; }
        public Fixture(string role = "Admin")
        {
            Repository = new(Audit);
            User = new(Guid.NewGuid(), "synthetic:reviewer", "test", AccessStatus.Approved, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, 1, [role], AccessRoleCatalog.GetCapabilities([role]));
            IApplicationAccessService access = Substitute.For<IApplicationAccessService>();
            access.GetCurrentAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<AccessOperationContext>(), Arg.Any<CancellationToken>())
                .Returns(AccessServiceResult<EnsureAccessUserResult>.Success(new(User, null, false, false)));
            IAccessRepository users = Substitute.For<IAccessRepository>();
            users.GetUserAsync(User.Id, Arg.Any<CancellationToken>()).Returns(User);
            Service = new(Repository, Source, access, users, new InMemoryCommandIdempotencyStore(TimeProvider.System), NullLogger<InUseService>.Instance);
        }
        public async Task<InUseRecord> ImportAsync()
        {
            Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(await new LocalInUseSourceClient().DiscoverAsync(_token));
            (await Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token)).Error.Should().BeNull();
            return (await Repository.QueryAsync(new(), User.Id, _token)).Items[0];
        }
    }
}
