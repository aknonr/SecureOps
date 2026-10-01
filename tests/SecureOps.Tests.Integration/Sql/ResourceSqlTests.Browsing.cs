using FluentAssertions;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Contracts.OperationalRecords;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    [LocalResourceSqlFact]
    public async Task StoredBrowsing_SqlHasStablePagesLiteralSearchAndPersistedTotals()
    {
        var repository = new SqlOperationalRecordRepository(Configuration());
        string unique = Guid.NewGuid().ToString("N");
        for (int i = 0; i < 12; i++)
        {
            await repository.UpsertImportedAsync(new OperationalRecordSourceItem($"synthetic-{unique}-{i:D2}", $"SYN-{unique}-{i:D2}",
                unique + " [literal]%", "Synthetic local browsing", null, null, null, null, null), "synthetic", _token);
        }
        var query = new OperationalRecordQuery { Search = unique + " [literal]%", Sort = "oldest", PageSize = 10 };
        OperationalRecordPage first = await repository.BrowseAsync(query, false, _token);
        OperationalRecordPage second = await new SqlOperationalRecordRepository(Configuration()).BrowseAsync(query with { Page = 2 }, false, _token);
        first.Total.Should().Be(12);
        first.Items.Should().HaveCount(10);
        second.Total.Should().Be(12);
        second.Items.Should().HaveCount(2);
        first.Items.Select(r => r.Id).Intersect(second.Items.Select(r => r.Id)).Should().BeEmpty();
        first.Items.Select(r => r.SourceRecordId).Should().BeInAscendingOrder();
        (await repository.BrowseAsync(query, true, _token)).Total.Should().Be(0);
        (await repository.BrowseAsync(query with { State = OperationalRecordWorkflowState.Completed }, false, _token)).Total.Should().Be(0);
        (await repository.BrowseAsync(query with { Page = 100000 }, false, _token)).Items.Should().BeEmpty();
        (await repository.GetAsync(first.Items[0].Id, _token))!.Version.Should().Be(first.Items[0].Version);
    }
}
