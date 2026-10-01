using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.OperationalRecords;

namespace SecureOps.Tests.Unit.OperationalRecords;

public sealed class OperationalRecordBrowsingTests
{
    [Fact]
    public async Task Browse_IsPersistedOnlyStableBoundedAndDoesNotAuditOrCallSource()
    {
        var repository = new InMemoryOperationalRecordRepository();
        for (int i = 0; i < 12; i++)
        {
            await repository.UpsertImportedAsync(TestRecord.SourceItem($"synthetic-{i:D2}", $"SYN-{i:D2}") with { Title = "Synthetic [literal]%" }, "synthetic", default);
        }
        IOperationalRecordClient source = Substitute.For<IOperationalRecordClient>();
        var audit = new InMemoryAuditWriter();
        var service = new OperationalRecordService(source, new ManualReviewOperationalRecordClassifier(), repository,
            audit, Options.Create(new OperationalRecordsOptions()), NullLogger<OperationalRecordService>.Instance);
        OperationalRecordPage first = await service.BrowseAsync(new() { Search = "[literal]%", Sort = "code", PageSize = 10 }, default);
        OperationalRecordPage second = await service.BrowseAsync(new() { Search = "[literal]%", Sort = "code", Page = 2, PageSize = 10 }, default);
        first.Total.Should().Be(12);
        second.Items.Should().HaveCount(2);
        first.Items.Select(r => r.Id).Intersect(second.Items.Select(r => r.Id)).Should().BeEmpty();
        (await service.BrowseAsync(new() { State = OperationalRecordWorkflowState.Completed }, default)).Total.Should().Be(0);
        (await repository.BrowseAsync(new(), true, default)).Total.Should().Be(0);
        audit.Events.Should().BeEmpty();
        source.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 25, "updated")]
    [InlineData(1, 101, "updated")]
    [InlineData(100001, 25, "updated")]
    [InlineData(1, 25, "arbitrary")]
    public async Task Browse_RejectsUnboundedOrUnknownQuery(int page, int size, string sort)
    {
        var repository = new InMemoryOperationalRecordRepository();
        await FluentActions.Invoking(() => repository.BrowseAsync(new() { Page = page, PageSize = size, Sort = sort }, false, default))
            .Should().ThrowAsync<ValidationException>();
    }
}
