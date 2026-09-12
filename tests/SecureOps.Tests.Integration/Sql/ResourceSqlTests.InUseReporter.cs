using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Commands;
using SecureOps.Infrastructure.InUse;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    [LocalResourceSqlFact]
    public Task InUse_ReporterRefresh_AdapterToSql_RetainsPartialEvidenceAndReviewer() => ReporterRoundTripAsync(false);

    [LocalResourceSqlFact]
    public Task InUse_ReporterAcceptance_NewFixtureOnly_PreservesExistingData() => ReporterRoundTripAsync(true);

    private async Task ReporterRoundTripAsync(bool newFixtureOnly)
    {
        IConfiguration configuration = Configuration();
        await using var connection = new SqlConnection(configuration.GetConnectionString("SecureOpsDb"));
        (Guid Id, string RecordJson)[] existing = newFixtureOnly ? (await connection.QueryAsync<(Guid Id, string RecordJson)>("SELECT Id, RecordJson FROM ops.InUseRecords")).ToArray() : [];
        Infrastructure.Resources.ResourceActor actor = await CreateActorAsync(connection);
        var users = new SqlAccessRepository(configuration);
        ApplicationUser user = (await users.GetUserAsync(actor.UserId, _token))!;
        var reviewers = new List<ApplicationUser>();
        CorporatePrincipal? trusted = null;
        foreach (string? name in new[] { "Önceki sentetik profil", "Örnek Gözlemci", "Örnek Gözlemci", null })
        {
            var identity = new CorporatePrincipal("oidc:synthetic-reporter-" + Guid.NewGuid().ToString("N"), "oidc", DisplayName: name);
            trusted ??= identity;
            EnsureAccessUserResult pending = await users.EnsureUserAsync(identity, true, TimeSpan.Zero, _token);
            AccessMutationResult approved = await users.DecideRequestAsync(pending.PendingRequest!.Id, AccessRequestStatus.Approved,
                pending.PendingRequest.Version, actor.UserId.ToString("D"), ["InUseReviewer"], "Synthetic reviewer", _token);
            reviewers.Add(approved.User!);
        }
        IApplicationAccessService access = Substitute.For<IApplicationAccessService>();
        access.GetCurrentAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<AccessOperationContext>(), Arg.Any<CancellationToken>())
            .Returns(AccessServiceResult<EnsureAccessUserResult>.Success(new(user with
            { Roles = ["Admin"], Capabilities = AccessRoleCatalog.GetCapabilities(["Admin"]) }, null, false, false)));
        string sourceId = Environment.GetEnvironmentVariable("SECUREOPS_INUSE_ACCEPTANCE_SOURCE_ID") ?? Random.Shared.NextInt64(930000000, 940000000).ToString();
        long.TryParse(sourceId, out long numericId).Should().BeTrue();
        numericId.Should().BeInRange(930000000, 939999999);
        using var handler = new ReporterHandler(sourceId);
        ITuruncuHatSessionManager sessions = Substitute.For<ITuruncuHatSessionManager>();
        sessions.GetSessionAsync(Arg.Any<CancellationToken>()).Returns("synthetic-session");
        var source = new TuruncuHatOperationalRecordClient(new HttpClient(handler) { BaseAddress = new Uri("https://source.invalid/") },
            sessions, Options.Create(new TuruncuHatOptions { RelatedGroupId = 68, ExcludedDccIds = [4241] }),
            Options.Create(new OperationalRecordsOptions { ReadOnlyIntegrationMode = true }),
            new EnterpriseIntegrationHealthState(), new EnterpriseIntegrationTelemetry(), NullLogger<TuruncuHatOperationalRecordClient>.Instance);
        IInUseRepository repository = newFixtureOnly ? new InMemoryInUseRepository(Substitute.For<IAuditWriter>()) : new SqlInUseRepository(configuration);
        var service = new InUseService(repository, source, access, users, new InMemoryCommandIdempotencyStore(TimeProvider.System), NullLogger<InUseService>.Instance);
        var principal = new ClaimsPrincipal();
        var context = new AccessOperationContext("synthetic-reporter", "local", null);
        async Task<InUseRecord> Refresh()
        {
            (await service.RefreshAsync(principal, context, new(Guid.NewGuid()), _token)).Error.Should().BeNull();
            IInUseRepository read = newFixtureOnly ? repository : new SqlInUseRepository(configuration);
            return (await read.QueryAsync(new(Search: "OR-" + sourceId), actor.UserId, _token)).Items.Single();
        }
        InUseRecord record = await Refresh();
        handler.TargetReads.Should().Be(2);
        record.Source.Servers.Select(s => s.RelatedRequestReporter!.Display).Should().Equal(
            "Sentetik &#350;ah&#305;s 200 &lt;b&gt;", "Sentetik &#350;ah&#305;s 200 &lt;b&gt;", "Sentetik &#350;ah&#305;s 201 &amp;lt;b&amp;gt;", null);
        InUseResult<InUseRecord> assigned = await service.AssignAsync(principal, context, record.Id, new(record.Version, reviewers[0].Id, "Synthetic manual assignment"), _token);
        assigned.Error.Should().BeNull();
        record = assigned.Value!;
        EnsureAccessUserResult refreshed = await users.EnsureUserAsync(trusted! with { DisplayName = "Sentetik İnceleyici" }, true, TimeSpan.Zero, _token);
        refreshed.User.Id.Should().Be(reviewers[0].Id);
        refreshed.User.Roles.Should().BeEquivalentTo(reviewers[0].Roles);
        IReadOnlyList<InUseAssignee> labels = (await service.AssigneesAsync(principal, context, _token)).Value!;
        string trustedLabel = labels.Single(p => p.Id == reviewers[0].Id).Label;
        trustedLabel.Should().StartWith("Sentetik İnceleyici");
        labels.Where(p => reviewers.Skip(1).Take(2).Any(u => u.Id == p.Id)).Select(p => p.Label).Distinct().Should().HaveCount(2);
        labels.Single(p => p.Id == reviewers[3].Id).Label.Should().StartWith("Profil adı bekleniyor");
        (await service.GetAsync(principal, context, record.Id, _token)).Value!.AssigneeLabel.Should().Be(trustedLabel);
        (await repository.GetAsync(record.Id, _token)).Should().BeEquivalentTo(record);
        string hash = record.SourceHash;
        long sourceVersion = record.SourceVersion;
        record = await Refresh();
        record.SourceHash.Should().Be(hash);
        record.SourceVersion.Should().Be(sourceVersion);
        record.AssigneeId.Should().Be(reviewers[0].Id);
        DateTimeOffset? verified = record.Source.Servers[2].RelatedRequestReporter!.LastVerifiedAt;
        handler.Partial = true;
        InUseRecord partial = await Refresh();
        partial.Source.Servers[2].RelatedRequestReporter.Should().BeEquivalentTo(record.Source.Servers[2].RelatedRequestReporter! with { State = "Forbidden", DisplayState = "Omitted", ReferenceState = "Omitted" });
        partial.Source.Servers[2].RelatedRequestReporter!.LastVerifiedAt.Should().Be(verified);
        partial.Source.Servers[0].RelatedRequestReporter!.State.Should().Be("ExactMatch");
        (await service.SaveDraftAsync(principal, context, record.Id, new(record.Version, record.SourceVersion, [], ""), _token)).Error.Should().Be("InUseConflict");
        int reads = handler.TargetReads;
        (await service.GetAsync(principal, context, record.Id, _token)).Value!.Source.Should().BeEquivalentTo(partial.Source);
        await service.QueryAsync(principal, context, new(), _token);
        handler.TargetReads.Should().Be(reads);
        handler.Outage = true;
        InUseRecord stale = await Refresh();
        stale.Source.Servers.Should().OnlyContain(s => s.RelatedRequestReporter!.State == "Stale");
        stale.LastSeenAt.Should().Be(partial.LastSeenAt);
        // Leave a current/partial synthetic snapshot for the loopback browser journey.
        handler.Outage = false;
        InUseRecord final = await Refresh();
        if (newFixtureOnly)
        {
            // Seed one NEW synthetic aggregate, without staling other retained fixtures through a global refresh.
            await connection.ExecuteAsync("""
                INSERT INTO ops.InUseRecords(Id, SourceId, Code, Title, AssigneeId, ReviewStatus, Version, RecordJson)
                VALUES(@Id, @SourceId, @Code, @Title, @AssigneeId, @ReviewStatus, @Version, @Json);
                """, new
            {
                final.Id,
                SourceId = final.Source.Id,
                final.Source.Code,
                final.Source.Title,
                final.AssigneeId,
                ReviewStatus = final.Status,
                final.Version,
                Json = JsonSerializer.Serialize(final)
            });
            var stored = new SqlInUseRepository(configuration);
            (await stored.GetAsync(final.Id, _token)).Should().BeEquivalentTo(final);
            var storedService = new InUseService(stored, source, access, users, new InMemoryCommandIdempotencyStore(TimeProvider.System), NullLogger<InUseService>.Instance);
            reads = handler.TargetReads;
            (await storedService.GetAsync(principal, context, final.Id, _token)).Value!.AssigneeLabel.Should().Be(trustedLabel);
            (await storedService.QueryAsync(principal, context, new(Search: final.Source.Code), _token)).Value!.Items.Single().Source.Should().BeEquivalentTo(final.Source);
            handler.TargetReads.Should().Be(reads);
            (await connection.QueryAsync<(Guid Id, string RecordJson)>("SELECT Id, RecordJson FROM ops.InUseRecords WHERE Id <> @Id", new { final.Id }))
                .Should().BeEquivalentTo(existing);
        }
    }

    private sealed class ReporterHandler(string sourceId) : HttpMessageHandler
    {
        public bool Partial { get; set; }
        public bool Outage { get; set; }
        public int TargetReads { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (Outage)
            { return new(HttpStatusCode.ServiceUnavailable); }
            using var query = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            string filter = query.RootElement.GetProperty("req").GetProperty("Filters")[0].GetString()!;
            object[][] rows;
            if (filter.Contains("m_active", StringComparison.Ordinal))
            { rows = [[Cell("SET.id", sourceId), Cell("SET.p_code", "OR-" + sourceId), Cell("SET.p_name", "Synthetic RFC reporter acceptance"), Cell("SET.p_description", "Synthetic only"), Cell("KEY.p_rel_requester", "Synthetic parent")]]; }
            else if (filter.Contains("m_lid", StringComparison.Ordinal))
            {
                rows = Enumerable.Range(1, 4).Select(i => new object[] { Cell("SET.(LCSIMS_ServiceInstance)m_rid.id", (92000 + i).ToString()),
                    Cell("SET.(LCSIMS_ServiceInstance)m_rid.p_name", "synthetic-rfc-" + i),
                    Cell("SET.(LCSIMS_ServiceInstance)m_rid.c_rfc_record", i == 4 ? null : i == 3 ? "OR-201" : "OR-200") }).Reverse().ToArray();
            }
            else
            {
                TargetReads++;
                bool second = filter == "#%p_code%#='OR-201'";
                if (Partial && second)
                { return new(HttpStatusCode.Forbidden); }
                rows = [[Cell("SET.p_rel_requester", second ? "801" : "800"), Cell("KEY.p_rel_requester", second ? "Sentetik &#350;ah&#305;s 201 &amp;lt;b&amp;gt;" : "Sentetik &#350;ah&#305;s 200 &lt;b&gt;"),
                    Cell("SET.id", second ? "201" : "200"), Cell("SET.p_code", second ? "OR-201" : "OR-200"), Cell("SET.m_active", "False")]];
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { QueryResult = new { Items = rows } })) };
        }
        private static object Cell(string key, string? value) => new { Key = key, Value = value };
    }
}
