using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Identity;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>Bounded directory name search (ADR-0025) with a synthetic directory and the real module repository and audit.</summary>
public sealed class ServiceAccountDirectorySearchSqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;

    [ServiceAccountSqlFact]
    public async Task NameSearch_LinksOnlyAccessibleRecords_AuditsWithoutNames_AndStaysBounded()
    {
        ServiceAccountSqlFixture fx = new();
        string tag = fx.Suffix.ToLowerInvariant();
        Guid org = await fx.OrganizationAsync("SYN DIR ORG " + fx.Suffix);
        Guid other = await fx.OrganizationAsync("SYN DIR DIGER " + fx.Suffix);
        SynUser coordinator = await fx.UserAsync(ServiceAccountCapabilities.View, ServiceAccountCapabilities.Assign, Capabilities.IdentityLookup);
        SynUser outsider = await fx.UserAsync(ServiceAccountCapabilities.View, ServiceAccountCapabilities.Assign, Capabilities.IdentityLookup);
        SynUser noLookup = await fx.UserAsync(ServiceAccountCapabilities.View);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        await fx.GrantAsync(outsider, ScopeKind.Organization, other);

        string inScope = "syn.ayse." + tag, outOfScope = "syn.ayse2." + tag, twoDomains = "syn.ayse3." + tag;
        Guid visible = Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context, new CreateAccountRequest(inScope, "SYN", org, "Sentetik"), _token))
            .Summary.Id;
        Ok(await fx.Service.CreateAccountAsync(outsider.Principal, fx.Context, new CreateAccountRequest(outOfScope, "SYN", other, "Sentetik"), _token));
        Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context, new CreateAccountRequest(twoDomains, "SYNA", org, "Sentetik"), _token));
        Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context, new CreateAccountRequest(twoDomains, "SYNB", org, "Sentetik"), _token));
        CountingDirectory directory = new(
        [
            new("Ayşe Yılmaz", "Ayşe", "Yılmaz", inScope, "SYN Altyapı"),
            new("Ayşe Yılmaz", "Ayşe", "Yılmaz", outOfScope, "SYN Uygulama"),
            new("Ayşe Yıldız", "Ayşe", "Yıldız", twoDomains, null),
            new("İsmail Işık", "İsmail", "Işık", "syn.ismail." + tag, null)
        ]);
        ServiceAccountService service = fx.WithDirectory(directory);

        DirectoryNameSearchResponse found = Ok(await service.DirectoryNameSearchAsync(coordinator.Principal, fx.Context, new("  AYŞE  "), _token));
        found.Matches.Select(m => m.Account).Should().Equal(twoDomains, inScope, outOfScope);
        DirectoryNameMatch linked = found.Matches.Single(m => m.Account == inScope);
        linked.ServiceAccountId.Should().Be(visible);
        linked.SameNameAsAnother.Should().BeTrue();
        linked.Department.Should().Be("SYN Altyapı");
        found.Matches.Single(m => m.Account == outOfScope).ServiceAccountId.Should().BeNull("a record outside the caller's scope is never revealed");
        found.Matches.Single(m => m.Account == twoDomains).ServiceAccountId.Should().BeNull("two accessible records with that name are ambiguous");
        (found.MinimumLetters, found.MaximumResults, found.Truncated).Should().Be((3, 10, false));

        Ok(await service.DirectoryNameSearchAsync(outsider.Principal, fx.Context, new("ayşe yılmaz"), _token)).Matches
            .Single(m => m.Account == inScope).ServiceAccountId.Should().BeNull("the outsider cannot reach the coordinator's record");
        Ok(await service.DirectoryNameSearchAsync(coordinator.Principal, fx.Context, new("ismail ışık"), _token)).Matches.Should().ContainSingle();

        // Authorization and validation happen before the directory is touched.
        int calls = directory.Calls;
        (await service.DirectoryNameSearchAsync(noLookup.Principal, fx.Context, new("ayşe"), _token)).ErrorCode.Should().Be(SaErrors.Forbidden);
        (await service.DirectoryNameSearchAsync(coordinator.Principal, fx.Context, new("ay"), _token)).Field.Should().Be("NameQueryTooShort");
        (await service.DirectoryNameSearchAsync(coordinator.Principal, fx.Context, new("ay*"), _token)).Field.Should().Be("NameQueryCharacters");
        (await service.DirectoryNameSearchAsync(coordinator.Principal, fx.Context, new("*)(objectClass=*"), _token)).Field.Should().Be("NameQueryCharacters");
        directory.Calls.Should().Be(calls);

        // Audit holds a hash and counts, never the name.
        await using (Microsoft.Data.SqlClient.SqlConnection connection = fx.Connection())
        {
            await connection.OpenAsync();
            string[] details = [.. await Dapper.SqlMapper.QueryAsync<string>(connection,
                "SELECT DetailsJson FROM audit.AuditLog WHERE CorrelationId = @id AND Action LIKE 'ServiceAccount.DirectoryNameSearch%'",
                new { id = fx.Context.CorrelationId })];
            details.Should().HaveCount(6, "requested + completed for each of the three searches");
            details.Should().OnlyContain(d => !d.Contains("Ay", StringComparison.OrdinalIgnoreCase) && !d.Contains("smail", StringComparison.OrdinalIgnoreCase)
                && !d.Contains("syn.", StringComparison.Ordinal));
        }

        // Unavailable or failing providers are reported as such and audited.
        (await fx.WithDirectory(null).DirectoryNameSearchAsync(coordinator.Principal, fx.Context, new("ayşe"), _token)).ErrorCode
            .Should().Be(SaErrors.DirectoryUnavailable);
        (await fx.WithDirectory(new FailingDirectory(new TimeoutException("synthetic"))).DirectoryNameSearchAsync(coordinator.Principal, fx.Context, new("ayşe"), _token))
            .ErrorCode.Should().Be(SaErrors.DirectoryUnavailable);
        (await fx.WithDirectory(new FailingDirectory(new InvalidOperationException("(displayName=Ayşe*)"))).DirectoryNameSearchAsync(coordinator.Principal, fx.Context,
            new("ayşe"), _token)).ErrorCode.Should().Be(SaErrors.DirectoryUnavailable);
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE CorrelationId = @id AND Action = 'ServiceAccount.DirectoryNameSearchFailed'",
            new { id = fx.Context.CorrelationId })).Should().Be(3);

        // A provider that over-answers is still cut to ten and flagged.
        DirectoryNameCandidate[] many = [.. Enumerable.Range(1, 14).Select(i => new DirectoryNameCandidate($"Deniz Sentetik{i:00}", "Deniz", $"Sentetik{i:00}",
            $"syn.deniz{i:00}.{tag}", null))];
        DirectoryNameSearchResponse bounded = Ok(await fx.WithDirectory(new OverAnsweringDirectory(many)).DirectoryNameSearchAsync(coordinator.Principal, fx.Context,
            new("deniz"), _token));
        bounded.Matches.Should().HaveCount(10);
        bounded.Truncated.Should().BeTrue();
    }

    [ServiceAccountSqlFact]
    public async Task AuditFailures_CloseTheSearchAtRequestedCompletedAndFailedStages()
    {
        ServiceAccountSqlFixture fx = new();
        SynUser user = await fx.UserAsync(ServiceAccountCapabilities.View, Capabilities.IdentityLookup);
        foreach (string stage in new[] { "Requested", "Completed", "Failed" })
        {
            string trigger = "sa_search_audit_" + Guid.NewGuid().ToString("N");
            CountingDirectory counted = new(MockDirectoryNameSearchProvider.DefaultUsers());
            IDirectoryNameSearchProvider directory = stage == "Failed" ? new FailingDirectory(new TimeoutException("synthetic")) : counted;
            await using Microsoft.Data.SqlClient.SqlConnection connection = fx.Connection();
            await connection.OpenAsync();
            // Identifiers and correlation are generated by this disposable fixture, never supplied by a caller.
            await Dapper.SqlMapper.ExecuteAsync(connection, $"""
                CREATE TRIGGER audit.[{trigger}] ON audit.AuditLog AFTER INSERT AS
                BEGIN
                    IF EXISTS (SELECT 1 FROM inserted WHERE CorrelationId = '{fx.Context.CorrelationId}'
                        AND Action = 'ServiceAccount.DirectoryNameSearch{stage}')
                        THROW 51091, 'Synthetic audit failure.', 1;
                END
                """);
            try
            {
                (await fx.WithDirectory(directory).DirectoryNameSearchAsync(user.Principal, fx.Context, new("ayşe"), _token))
                    .ErrorCode.Should().Be(SaErrors.Unavailable);
                counted.Calls.Should().Be(stage == "Completed" ? 1 : 0);
            }
            finally
            {
                await Dapper.SqlMapper.ExecuteAsync(connection, $"DROP TRIGGER audit.[{trigger}];");
            }
        }
    }

    private sealed class CountingDirectory(IEnumerable<DirectoryNameCandidate> users) : IDirectoryNameSearchProvider
    {
        private readonly MockDirectoryNameSearchProvider _inner = new(users);
        public int Calls { get; private set; }

        public Task<DirectoryNameSearchResult> SearchAsync(DirectoryNameQuery query, int limit, CancellationToken cancellationToken)
        {
            Calls++;
            return _inner.SearchAsync(query, limit, cancellationToken);
        }
    }

    private sealed class FailingDirectory(Exception failure) : IDirectoryNameSearchProvider
    {
        public Task<DirectoryNameSearchResult> SearchAsync(DirectoryNameQuery query, int limit, CancellationToken cancellationToken) => Task.FromException<DirectoryNameSearchResult>(failure);
    }

    private sealed class OverAnsweringDirectory(IReadOnlyList<DirectoryNameCandidate> all) : IDirectoryNameSearchProvider
    {
        public Task<DirectoryNameSearchResult> SearchAsync(DirectoryNameQuery query, int limit, CancellationToken cancellationToken) =>
            Task.FromResult(new DirectoryNameSearchResult(all, false));
    }

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }
}
