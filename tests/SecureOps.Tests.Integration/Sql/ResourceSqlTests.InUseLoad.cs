using System.Diagnostics;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.InUse;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Infrastructure.Resources;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    [LocalResourceSqlFact]
    public async Task InUse_RepresentativeSearchLoad_RetainsBoundedPagesAndCapturesCachedPlans()
    {
        await using SqlConnection connection = new(Configuration().GetConnectionString("SecureOpsDb"));
        ResourceActor actor = await CreateActorAsync(connection);
        var repository = new SqlInUseRepository(Configuration());
        InUseSource template = (await new LocalInUseSourceClient().DiscoverAsync(_token)).Records[0];
        string prefix = "LOAD-" + Guid.NewGuid().ToString("N");
        InUseSource[] sources = Enumerable.Range(0, 1000).Select(i => template with
        {
            Id = prefix + i,
            Code = prefix + i.ToString("D4"),
            Title = "Synthetic representative search",
            Servers = template.Servers.Select(s => s with
            {
                RelatedRequestReporter = new(prefix + i, s.Id, "OR-123", "OrCode", "123", "OR-123",
                i % 10 == 0 ? prefix + " I\u015f\u0131k" : "Synthetic reporter", "synthetic-account", "ExactMatch", "Returned", "Returned", DateTimeOffset.UtcNow)
            }).ToArray()
        }).ToArray();
        var audit = new AuditEvent { Actor = actor.UserId.ToString("D"), Action = "InUseLoadTest", CorrelationId = prefix };
        (await repository.RefreshAsync((await repository.StateAsync(_token)).Version, new(sources, false), null, audit, _token)).Should().BeTrue();
        List<double> elapsed = [];
        foreach (int page in Enumerable.Range(1, 20))
        {
            var watch = Stopwatch.StartNew();
            InUsePage result = await repository.QueryAsync(new(Search: prefix + " \u0131\u015f\u0131k", Page: page, PageSize: 5), actor.UserId, _token);
            elapsed.Add(watch.Elapsed.TotalMilliseconds);
            result.Total.Should().Be(100);
            result.Items.Should().HaveCount(5);
        }
        // LocalDB test-owner-only measurement. Runtime needs no DMV or plan permissions.
        string[] plans = [.. await connection.QueryAsync<string>("""
            SELECT DISTINCT CONVERT(nvarchar(max),p.query_plan) FROM sys.dm_exec_cached_plans AS c
            CROSS APPLY sys.dm_exec_sql_text(c.plan_handle) AS t
            CROSS APPLY sys.dm_exec_query_plan(c.plan_handle) AS p
            WHERE t.dbid=DB_ID() AND t.text LIKE '%SELECT COUNT(*)%FROM ops.InUseRecords WHERE%'
            """)];
        plans.Should().NotBeEmpty();
        if (Environment.GetEnvironmentVariable("SECUREOPS_INUSE_LOAD_EVIDENCE") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, prefix + ".json"), JsonSerializer.Serialize(new
            { Rows = 1000, ServersPerRecord = template.Servers.Count, Queries = 20, Milliseconds = elapsed, CachedPlans = plans.Length, CorporateScaleProven = false }));
            for (int i = 0; i < plans.Length; i++)
            { await File.WriteAllTextAsync(Path.Combine(directory, prefix + "-" + i + ".sqlplan"), plans[i]); }
        }
    }
}
