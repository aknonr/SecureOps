using System.Text;
using FluentAssertions;
using NSubstitute;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.DirectoryExplorer;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Directory;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>
/// "Hangi sunucularda?" on the real svcacct schema (synthetic data): the stored sources (an attached scan, an Excel finding, a
/// manual usage) and the directory SPN become one row per server without overwriting each other, a source that cannot be read
/// says so without hiding the others, out-of-scope accounts are indistinguishable from missing, and nothing is written.
/// </summary>
public sealed class ServiceAccountWhereUsedSqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;
    private static readonly string[] _all = [.. ServiceAccountCapabilities.All];
    private static readonly string[] _allWithDirectory = [.. ServiceAccountCapabilities.All, Capabilities.DirectoryGroupsView];

    [ServiceAccountSqlFact]
    public async Task ThreeStoredSourcesAndSpn_BecomeOneRowPerServer_WithExplicitConflictAndNoWrites()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        SynUser reader = await fx.UserAsync(_allWithDirectory);
        await fx.GrantAsync(reader, ScopeKind.Organization, org);
        AccountDetail account = await SeedAsync(fx, coordinator, org, "FULL");
        Guid id = account.Summary.Id;
        IDirectoryEnrichmentQueryService directory = Directory(["HTTP/syn-app02.corp.example:8080", "MSSQLSvc/SYN-NEW01:1433", "broken"]);
        int historyBefore = await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE AccountId = @id", new { id });
        int usagesBefore = await fx.CountAsync("SELECT COUNT(*) FROM svcacct.AccountUsages WHERE AccountId = @id", new { id });

        WhereUsedView view = Ok(await fx.WithServiceEvidence(directory).WhereUsedAsync(reader.Principal, fx.Context, id, null, false, 1, null, _token));

        view.Servers.Select(s => $"{s.Name}:{s.Status}").Should().Equal(
            "syn-app02.corp.example:Conflict",
            "syn-app01.corp.example:Found",
            "SYN-NEW01:DefinedOnly",
            "SYN-APP03:NoInformation",
            "SYN-APP04:NoInformation");
        WhereUsedServer conflict = view.Servers[0];
        conflict.Evidence.Select(e => e.Source + "/" + e.Outcome).Should().Equal("Scan/NotFound", "Finding/NotFound", "Manual/Found", "Spn/Defined");
        conflict.ConflictText.Should().Contain("Elle kayıt").And.Contain("Tarama-Betik").And.Contain("kullanılmadığını göstermez");
        conflict.AlsoSeenAs.Should().Contain("SYN-APP02");
        WhereUsedServer found = view.Servers[1];
        found.SourceCount.Should().Be(2);
        found.Evidence[0].Should().Match<WhereUsedEvidence>(e => e.Source == "Scan" && e.Tool == "Combined" && e.ComponentCount >= 1 && e.Components.Count == e.ComponentCount);
        found.Evidence[1].Should().Match<WhereUsedEvidence>(e => e.Source == "Finding" && e.Outcome == "Found");
        found.LastSeenAt.Should().NotBeNull();
        view.Summary.Should().Match<WhereUsedSummary>(s => s.Servers == 5 && s.Found == 2 && s.Conflicts == 1 && s.DefinedOnly == 1 && s.NoInformation == 2
            && s.ScansIncluded == 1 && s.ScansOmitted == 0 && s.LastScanAt != null);
        view.Sources.Single(s => s.Source == "Spn").Should().Match<WhereUsedSourceStatus>(s => s.State == "Partial" && s.Text.Contains("1 SPN okunamadı"));
        view.Sources.Where(s => s.Source != "Spn").Should().OnlyContain(s => s.State == "Available");

        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE AccountId = @id", new { id })).Should().Be(historyBefore, "reading writes no history");
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.AccountUsages WHERE AccountId = @id", new { id })).Should().Be(usagesBefore);
    }

    [ServiceAccountSqlFact]
    public async Task FiltersAndPaging_AreServerSide_AndSummaryCountsStayWholeAccount()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        AccountDetail account = await SeedAsync(fx, coordinator, org, "PAGE");
        Guid id = account.Summary.Id;

        WhereUsedView found = Ok(await fx.Service.WhereUsedAsync(coordinator.Principal, fx.Context, id, null, true, 1, null, _token));
        found.Servers.Select(s => s.Status).Should().Equal("Conflict", "Found");
        found.Total.Should().Be(2);
        found.Summary.Servers.Should().Be(4, "the summary is over every server of the account, not the filter");

        WhereUsedView manual = Ok(await fx.Service.WhereUsedAsync(coordinator.Principal, fx.Context, id, "Manual", false, 1, null, _token));
        manual.Servers.Should().ContainSingle().Which.Status.Should().Be("Conflict");

        WhereUsedView firstPage = Ok(await fx.Service.WhereUsedAsync(coordinator.Principal, fx.Context, id, null, false, 1, 3, _token));
        WhereUsedView secondPage = Ok(await fx.Service.WhereUsedAsync(coordinator.Principal, fx.Context, id, null, false, 2, 3, _token));
        (firstPage.Servers.Count, secondPage.Servers.Count, firstPage.Total, secondPage.Total).Should().Be((3, 1, 4, 4));
        firstPage.Servers.Concat(secondPage.Servers).Select(s => s.Name).Should().OnlyHaveUniqueItems();

        foreach ((string? source, int page, int? size, string field) in new (string?, int, int?, string)[]
        {
            ("Dna", 1, null, "source"), (null, 0, null, "page"), (null, 1, 0, "pageSize"), (null, 1, 101, "pageSize")
        })
        {
            SaResult<WhereUsedView> refused = await fx.Service.WhereUsedAsync(coordinator.Principal, fx.Context, id, source, false, page, size, _token);
            (refused.ErrorCode, refused.Field).Should().Be((SaErrors.Invalid, field));
        }
    }

    [ServiceAccountSqlFact]
    public async Task UnreadableDirectory_OnlyMakesSpnUnknown_AndMissingCapabilityIsDenied_NotEmpty()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        SynUser reader = await fx.UserAsync(_allWithDirectory);
        await fx.GrantAsync(reader, ScopeKind.Organization, org);
        AccountDetail account = await SeedAsync(fx, coordinator, org, "SPN");
        Guid id = account.Summary.Id;

        IDirectoryEnrichmentQueryService down = Substitute.For<IDirectoryEnrichmentQueryService>();
        down.GetServiceEvidenceAsync(Arg.Any<DirectoryPrincipalEnrichmentRequest>(), Arg.Any<DirectoryQueryExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(DirectoryQueryResult<DirectoryServiceEvidenceResponse>.Failure(DirectoryQueryStatus.ProviderUnavailable, "DirectoryUnavailable"));
        IDirectoryEnrichmentQueryService throws = Substitute.For<IDirectoryEnrichmentQueryService>();
        throws.GetServiceEvidenceAsync(Arg.Any<DirectoryPrincipalEnrichmentRequest>(), Arg.Any<DirectoryQueryExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<DirectoryQueryResult<DirectoryServiceEvidenceResponse>>>(_ => throw new TimeoutException("syn-ldap-detail"));
        IDirectoryEnrichmentQueryService missing = Substitute.For<IDirectoryEnrichmentQueryService>();
        missing.GetServiceEvidenceAsync(Arg.Any<DirectoryPrincipalEnrichmentRequest>(), Arg.Any<DirectoryQueryExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(DirectoryQueryResult<DirectoryServiceEvidenceResponse>.Failure(DirectoryQueryStatus.NotFound, "DirectoryPrincipalNotFound"));

        foreach (IDirectoryEnrichmentQueryService provider in new[] { down, throws, missing })
        {
            WhereUsedView view = Ok(await fx.WithServiceEvidence(provider).WhereUsedAsync(reader.Principal, fx.Context, id, null, false, 1, null, _token));
            view.Sources.Single(s => s.Source == "Spn").Should().Match<WhereUsedSourceStatus>(s => s.State == "Unknown" && s.Text.Contains("SPN bilinmiyor"));
            view.Sources.Single(s => s.Source == "Spn").Text.Should().NotContain("syn-ldap-detail");
            view.Servers.Should().HaveCount(4, "the stored sources are not affected");
        }

        WhereUsedView noCapability = Ok(await fx.WithServiceEvidence(down).WhereUsedAsync(coordinator.Principal, fx.Context, id, null, false, 1, null, _token));
        noCapability.Sources.Single(s => s.Source == "Spn").State.Should().Be("Denied");
        await down.ReceivedWithAnyArgs(1).GetServiceEvidenceAsync(default!, default!, default);

        WhereUsedView notConfigured = Ok(await fx.WithServiceEvidence(null).WhereUsedAsync(reader.Principal, fx.Context, id, null, false, 1, null, _token));
        notConfigured.Sources.Single(s => s.Source == "Spn").State.Should().Be("NotConfigured");
        notConfigured.Servers.Should().HaveCount(4);
    }

    [ServiceAccountSqlFact]
    public async Task OutOfScopeOrMissingAccount_IsNotFound_AndViewCapabilityIsRequired()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        AccountDetail account = await SeedAsync(fx, coordinator, org, "SCOPE");
        Guid id = account.Summary.Id;

        SynUser outsider = await fx.UserAsync(_all);
        await fx.GrantAsync(outsider, ScopeKind.Team, team: await fx.TeamAsync("SYN WU FARK " + fx.Suffix, null));
        SynUser noView = await fx.UserAsync(ServiceAccountCapabilities.Work);
        await fx.GrantAsync(noView, ScopeKind.Organization, org);

        (await fx.Service.WhereUsedAsync(outsider.Principal, fx.Context, id, null, false, 1, null, _token)).ErrorCode.Should().Be(SaErrors.NotFound);
        (await fx.Service.WhereUsedAsync(coordinator.Principal, fx.Context, Guid.NewGuid(), null, false, 1, null, _token)).ErrorCode.Should().Be(SaErrors.NotFound);
        (await fx.Service.WhereUsedAsync(noView.Principal, fx.Context, id, null, false, 1, null, _token)).ErrorCode.Should().Be(SaErrors.Forbidden);
    }

    [ServiceAccountSqlFact]
    public async Task AccountWithoutAnySource_ListsNothing_AndSaysEachSourceIsEmpty_NotUnused()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        AccountDetail account = Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context,
            new CreateAccountRequest($"SYN_WU_EMPTY_{fx.Suffix}", "SYN", org, "Sentetik boş hesap"), _token));

        WhereUsedView view = Ok(await fx.Service.WhereUsedAsync(coordinator.Principal, fx.Context, account.Summary.Id, null, false, 1, null, _token));

        view.Servers.Should().BeEmpty();
        view.Summary.Should().Match<WhereUsedSummary>(s => s.Servers == 0 && s.LastScanAt == null && s.ScansIncluded == 0);
        view.Sources.Where(s => s.Source != "Spn").Should().OnlyContain(s => s.State == "Empty");
    }

    private static IDirectoryEnrichmentQueryService Directory(string[] spns)
    {
        IDirectoryEnrichmentQueryService directory = Substitute.For<IDirectoryEnrichmentQueryService>();
        directory.GetServiceEvidenceAsync(Arg.Any<DirectoryPrincipalEnrichmentRequest>(), Arg.Any<DirectoryQueryExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(DirectoryQueryResult<DirectoryServiceEvidenceResponse>.Success(new DirectoryServiceEvidenceResponse(spns, spns.Length, false, null, null, null, null,
                "ServiceAccountEvidence", null, null, null, false)));
        return directory;
    }

    /// <summary>
    /// A scan (APP01 found, APP02 completely scanned without a match, APP03 partial, APP04 unreachable), an Excel finding that
    /// found the account on APP01's FQDN, an Excel finding that did not find it on APP02, and a manual usage on APP02.
    /// </summary>
    private static async Task<AccountDetail> SeedAsync(ServiceAccountSqlFixture fx, SynUser coordinator, Guid org, string label)
    {
        AccountDetail account = Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context,
            new CreateAccountRequest($"SYN_WU_{label}_{fx.Suffix}", "SYN", org, "Sentetik nerede kullanılıyor testi"), _token));
        Guid id = account.Summary.Id;
        Ok(await fx.Service.AttachUsageScanAsync(coordinator.Principal, fx.Context, id, "scan.json", Example(account.Summary.AccountName), "Sentetik: test", null, _token));
        Ok(await fx.Service.CreateFindingAsync(coordinator.Principal, fx.Context, new CreateFindingRequest(id, "Success", "Match", Server: "syn-app01.corp.example",
            ComponentType: "WindowsService", ComponentName: "SynSvc", ScanAt: DateTimeOffset.UtcNow.AddDays(-1)), _token));
        Ok(await fx.Service.CreateFindingAsync(coordinator.Principal, fx.Context, new CreateFindingRequest(id, "Success", "NoMatch", Server: "syn-app02",
            ScanAt: DateTimeOffset.UtcNow.AddDays(-2)), _token));
        return Ok(await fx.Service.CreateUsageAsync(coordinator.Principal, fx.Context, id, new CreateUsageRequest("ScheduledTask", Server: "SYN-APP02", Component: "SynTask"),
            _token));
    }

    private static async Task<(ServiceAccountSqlFixture, SynUser, Guid)> SetupAsync()
    {
        ServiceAccountSqlFixture fx = new();
        Guid org = await fx.OrganizationAsync("SYN WU ORG " + fx.Suffix);
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        return (fx, coordinator, org);
    }

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }

    private static byte[] Example(string name)
    {
        string text = File.ReadAllText(Path.Combine(Root(), "contracts", "examples", "service-account-usage-scan-example.json"))
            .Replace("SYN\\\\svc_synapp", $"SYN\\\\{name}", StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes(text);
    }

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
