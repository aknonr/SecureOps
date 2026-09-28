using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.InUse;
using SecureOps.Infrastructure.Resources;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    [Fact]
    public Task InUse_MemoryOrdering_UsesSourceDatesBeforePaging() =>
        VerifyRequestOrderingAsync(new InMemoryInUseRepository(new InMemoryAuditWriter()), Guid.NewGuid());

    [LocalResourceSqlFact]
    public async Task InUse_SqlOrdering_UsesSourceDatesBeforePaging()
    {
        await using SqlConnection connection = new(Configuration().GetConnectionString("SecureOpsDb"));
        ResourceActor actor = await CreateActorAsync(connection);
        await VerifyRequestOrderingAsync(new SqlInUseRepository(Configuration()), actor.UserId);
    }

    private static async Task VerifyRequestOrderingAsync(IInUseRepository repository, Guid actor)
    {
        string prefix = "ORDER-" + Guid.NewGuid().ToString("N");
        InUseSource seed = (await new LocalInUseSourceClient().DiscoverAsync(_token)).Records[0];
        InUseSource[] sources = Enumerable.Range(0, 30).Select(i => seed with
        {
            Id = prefix + i, Code = prefix + (29 - i).ToString("D2"),
            Creation = new(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i).ToString("O"), "Synthetic parent creation"),
            WasasActivity = new("Pending", "Synthetic eligible WASAS activity"), Lifecycle = new("Open", "Synthetic parent")
        }).ToArray();
        sources[28] = sources[28] with { Code = sources[27].Code, Creation = new("2000-01-28T03:00:00+03:00", "Synthetic parent creation") };
        sources[29] = sources[29] with { Code = sources[27].Code, Creation = new("2000-01-28T00:00:00Z", "Synthetic parent creation") };
        InUseEvidence?[] invalid = [null, new(null, "fixture"), new("invalid", "fixture"),
            new("2000-01-01T00:00:00", "fixture"), new("9999-01-01T00:00:00Z", "fixture"),
            new("2000-01-01T00:00:00Z", ""), new("2000-01-01T00:00:00Z", " "),
            new("2000-01-01 00:00:00Z", "fixture"), new("2000-01-01T00:00:00.1Z", "fixture"),
            new("2000-01-01T00:00:00Z", "\t\r\n\u00a0\u2003"), new("2000/01/01T00:00:00Z", "fixture")];
        InUseSource[] unknown = invalid.Select((date, i) => seed with
        { Id = prefix + "unknown" + i, Code = prefix + "00-unknown" + i.ToString("D2"), Creation = date }).ToArray();
        var audit = new AuditEvent { Actor = actor.ToString("D"), Action = "SyntheticInUseOrdering", CorrelationId = prefix };
        (await repository.RefreshAsync((await repository.StateAsync(_token)).Version, new([.. sources, .. unknown], false), null, audit, _token)).Should().BeTrue();
        InUsePage all = await repository.QueryAsync(new(Search: prefix, PageSize: 100, Sort: "oldest"), actor, _token);
        all.Total.Should().Be(30 + invalid.Length);
        all.Items.Take(27).Select(r => r.Source.Id).Should().Equal(sources.Take(27).Select(s => s.Id));
        InUseRecord[] tied = all.Items.Skip(27).Take(3).ToArray();
        tied.Select(r => r.Id.ToString("D")).Should().BeInAscendingOrder(StringComparer.Ordinal);
        all.Items.Skip(30).Select(r => r.Source.Id).Should().Equal(unknown.Select(s => s.Id));
        InUseRecord[] codeOrder = all.Items.OrderBy(r => r.Source.Code, StringComparer.Ordinal)
            .ThenBy(r => r.Id.ToString("D"), StringComparer.Ordinal).ToArray();
        for (int page = 1; page <= (int)Math.Ceiling(all.Total / 7.0); page++)
        {
            (await repository.QueryAsync(new(Search: prefix, Page: page, PageSize: 7), actor, _token)).Items.Select(r => r.Id)
                .Should().Equal(codeOrder.Skip((page - 1) * 7).Take(7).Select(r => r.Id));
        }
        foreach (string sort in new[] { "oldest", "newest" })
        {
            var paged = new List<InUseRecord>();
            for (int page = 1; page <= (int)Math.Ceiling(all.Total / 7.0); page++)
            {
                InUsePage result = await repository.QueryAsync(new(Search: prefix, Page: page, PageSize: 7, Sort: sort), actor, _token);
                result.Total.Should().Be(all.Total);
                paged.AddRange(result.Items);
            }
            IEnumerable<InUseRecord> expected = sort == "oldest" ? all.Items.Take(30) : tied.Concat(all.Items.Take(27).Reverse());
            paged.Select(r => r.Id).Should().Equal(expected.Concat(all.Items.Skip(30)).Select(r => r.Id));
            paged.Select(r => r.Id).Should().OnlyHaveUniqueItems();
        }
        // A later refresh must neither become the request date nor disturb an equal-date tie.
        await repository.RefreshAsync((await repository.StateAsync(_token)).Version, new([.. sources, .. unknown], false), null, audit, _token);
        (await repository.QueryAsync(new(Search: prefix, PageSize: 100, Sort: "oldest"), actor, _token)).Items.Select(r => r.Id).Should().Equal(all.Items.Select(r => r.Id));
    }

    [Fact]
    public Task InUse_MemoryActivity_ReconcilesOnlyObservedCompletion() =>
        VerifyActivityRefreshAsync(new InMemoryInUseRepository(new InMemoryAuditWriter()), Guid.NewGuid());

    [LocalResourceSqlFact]
    public async Task InUse_SqlActivity_ReconcilesOnlyObservedCompletion()
    {
        await using SqlConnection connection = new(Configuration().GetConnectionString("SecureOpsDb"));
        ResourceActor actor = await CreateActorAsync(connection);
        await VerifyActivityRefreshAsync(new SqlInUseRepository(Configuration()), actor.UserId);
    }

    private static async Task VerifyActivityRefreshAsync(IInUseRepository repository, Guid actor)
    {
        string prefix = "ACTIVITY-" + Guid.NewGuid().ToString("N");
        InUseSource source = (await new LocalInUseSourceClient().DiscoverAsync(_token)).Records[0] with
        { Id = prefix, Code = prefix, WasasActivity = new("Pending", "Synthetic bounded WASAS query"), Lifecycle = new("Open", "Synthetic parent") };
        var audit = new AuditEvent { Actor = actor.ToString("D"), Action = "SyntheticInUseActivity", CorrelationId = prefix };
        async Task Refresh(InUseSource[] rows) => (await repository.RefreshAsync((await repository.StateAsync(_token)).Version, new(rows, true), null, audit, _token)).Should().BeTrue();
        async Task<InUsePage> View(string view) => await repository.QueryAsync(new(Search: prefix, View: view), actor, _token);
        await Refresh([source]);
        InUseRecord initial = (await View("pending")).Items.Single();
        var draft = new InUseDraft(initial.SourceVersion, [new(source.Servers[0].Id, "InternetOut", "No", "Synthetic saved answer")], "retained", actor, DateTimeOffset.UtcNow);
        (await repository.SaveAsync(initial with { Version = initial.Version + 1, Draft = draft }, initial.Version, audit, _token)).Should().BeTrue();
        await Refresh([]);
        (await View("pending")).Total.Should().Be(0);
        (await View("tracking")).Total.Should().Be(0);
        InUseRecord missing = (await View("verification")).Items.Single();
        missing.SourceObservationMissing.Should().BeTrue();
        missing.LastSeenAt.Should().Be(initial.LastSeenAt);
        missing.Draft.Should().BeEquivalentTo(draft);
        missing.ReviewCurrent.Should().BeTrue();
        foreach (InUseEvidence evidence in new InUseEvidence[] { new("Completed", ""), new("Completed", "\t\r\n\u00a0\u2003"),
            new("completed", "fixture"), new("Completed ", "fixture"), new("pending", "fixture"), new("Pending ", "fixture") })
        {
            await Refresh([source with { WasasActivity = evidence }]);
            (await View("tracking")).Total.Should().Be(0, "provenance and normalized status must agree across SQL and memory");
            (await View("pending")).Total.Should().Be(0);
            (await View("verification")).Items.Single().ActivityStatus.Should().Be("VerificationPending");
        }
        await Refresh([source with { WasasActivity = new("Completed", "Synthetic bounded completed WASAS query") }]);
        InUseRecord completed = (await View("tracking")).Items.Single();
        completed.ActivityStatus.Should().Be("Completed");
        completed.Source.Lifecycle!.Value.Should().Be("Open");
        completed.Draft.Should().BeEquivalentTo(draft);
        completed.ReviewCurrent.Should().BeTrue();
        (await View("pending")).Total.Should().Be(0);
        (await View("verification")).Total.Should().Be(0);
        await Refresh([]);
        (await View("tracking")).Items.Single().SourceObservationMissing.Should().BeTrue("historical completion evidence remains distinct from current observation");
    }
}
