using System.Xml.Linq;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using Xunit.Abstractions;

namespace SecureOps.Tests.Integration.Sql;

public sealed class AccessRegistrationSqlTests(ITestOutputHelper output)
{
    [AccessRegistrationSqlFact]
    public async Task EnsureUserAsync_ConcurrentFirstRegistrations_OnePendingRequestAndHistoryPerUser()
    {
        string connectionString = Environment.GetEnvironmentVariable("SECUREOPS_ACCESS_REGISTRATION_CONNECTION")!;
        var guard = new SqlConnectionStringBuilder(connectionString);
        guard.DataSource.Should().Be("(localdb)\\SecureOpsAccessG34");
        guard.InitialCatalog.Should().StartWith("SecureOps_AccessG34_");
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SecureOpsDb"] = connectionString
        }).Build();
        var repository = new SqlAccessRepository(configuration);
        string prefix = $"g34:{Guid.NewGuid():N}";
        const int parallelism = 8;
        const int rounds = 10;

        // Separate identity-index gaps so Users does not serialize every first registration.
        for (int i = 0; i <= parallelism * rounds; i++)
        {
            await repository.EnsureUserAsync(new CorporatePrincipal($"{prefix}:{i:D4}:seed", "synthetic"), true, TimeSpan.FromMinutes(5), default);
        }

        await using var sql = new SqlConnection(connectionString);
        // Keep first registrations overlapping even when LocalDB answers faster than task scheduling.
        await sql.ExecuteAsync($"""
            CREATE TRIGGER security.TR_G34_RegistrationOverlap ON security.Users AFTER INSERT AS
            BEGIN
                IF EXISTS (SELECT 1 FROM inserted WHERE CorporateIdentity LIKE '{prefix}:%:new')
                    WAITFOR DELAY '00:00:00.050';
            END;
            """);
        var failures = new List<Exception>();
        var results = new List<EnsureAccessUserResult>();
        try
        {
            for (int round = 0; round < rounds; round++)
            {
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<(EnsureAccessUserResult? Result, Exception? Error)>[] calls = Enumerable.Range(round * parallelism, parallelism)
                    .Select(async i =>
                    {
                        await start.Task;
                        try
                        {
                            EnsureAccessUserResult result = await repository.EnsureUserAsync(new CorporatePrincipal($"{prefix}:{i:D4}:new", "synthetic"), true, TimeSpan.FromMinutes(5), default);
                            return (Result: (EnsureAccessUserResult?)result, Error: (Exception?)null);
                        }
                        catch (Exception exception)
                        {
                            return (Result: (EnsureAccessUserResult?)null, Error: (Exception?)exception);
                        }
                    }).ToArray();
                start.SetResult();
                foreach ((EnsureAccessUserResult? Result, Exception? Error) call in await Task.WhenAll(calls))
                {
                    if (call.Error is not null)
                    { failures.Add(call.Error); }
                    else
                    { results.Add(call.Result!); }
                }
            }
        }
        finally { await sql.ExecuteAsync("DROP TRIGGER security.TR_G34_RegistrationOverlap;"); }

        string? events = await sql.QuerySingleOrDefaultAsync<string>("""
            SELECT CONVERT(nvarchar(max), t.target_data)
            FROM sys.dm_xe_sessions s JOIN sys.dm_xe_session_targets t ON t.event_session_address = s.address
            WHERE s.name = 'SecureOpsAccessG34' AND t.target_name = 'ring_buffer';
            """);
        events.Should().NotBeNull("the harness must capture deadlocks rather than treating missing diagnostics as success");
        int deadlocks = events is null ? 0 : XDocument.Parse(events).Descendants("event").Count();
        string? evidenceDirectory = Environment.GetEnvironmentVariable("SECUREOPS_ACCESS_REGISTRATION_EVIDENCE");
        if (evidenceDirectory is not null && events is not null)
        {
            Directory.CreateDirectory(evidenceDirectory);
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "deadlocks.xml"), events);
        }
        output.WriteLine($"Registrations={parallelism * rounds}; Succeeded={results.Count}; Errors={failures.Count}; DeadlockEvents={deadlocks}");
        foreach (Exception error in failures)
        { output.WriteLine($"ErrorType={error.GetType().Name}; Number={(error as SqlException)?.Number}"); }
        failures.Should().BeEmpty("all eight concurrent first registrations must succeed in every round");
        deadlocks.Should().Be(0, "a retry must not conceal a recurring database deadlock");
        results.Should().HaveCount(parallelism * rounds);
        foreach (EnsureAccessUserResult result in results)
        {
            result.UserCreated.Should().BeTrue();
            result.RequestCreated.Should().BeTrue();
            result.User.Status.Should().Be(AccessStatus.Pending);
            result.User.Roles.Should().BeEmpty();
            IReadOnlyList<ApplicationAccessRequest> requests = await repository.ListRequestsForUserAsync(result.User.Id, default);
            requests.Should().ContainSingle().Which.Status.Should().Be(AccessRequestStatus.Pending);
            (await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM security.AccessRequestHistory WHERE AccessRequestId=@Id", new { Id = result.LatestRequest!.Id })).Should().Be(1);
            EnsureAccessUserResult repeated = await repository.EnsureUserAsync(new CorporatePrincipal(result.User.CorporateIdentity, "synthetic"), true, TimeSpan.FromMinutes(5), default);
            repeated.UserCreated.Should().BeFalse();
            repeated.RequestCreated.Should().BeFalse();
            repeated.User.Id.Should().Be(result.User.Id);
            repeated.LatestRequest!.Id.Should().Be(result.LatestRequest.Id);
        }
    }
}

public sealed class AccessRegistrationSqlFactAttribute : FactAttribute
{
    public AccessRegistrationSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SECUREOPS_ACCESS_REGISTRATION_CONNECTION")))
        {
            Skip = "Requires the fresh synthetic LocalDB access-registration harness.";
        }
    }
}
