using FluentAssertions;
using NSubstitute;
using SecureOps.Infrastructure.InUse;
using SecureOps.Infrastructure.ItsmRecords;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Unit.InUse;

// TH-11: known, still listed records that discovery no longer returns are re-checked by id, bounded and fail closed.
public sealed partial class InUseTests
{
    private static InUseEvidencePatch LeftTeam() => new(null, null, new(InUseActivity.NotAssignedToWasas, "Synthetic rule"));

    private static async Task<(Fixture Fixture, InUseRecord Left, InUseRecord Stays)> TwoKnownAsync()
    {
        var f = new Fixture();
        await f.ImportAsync();
        InUsePage page = await f.Repository.QueryAsync(new(PageSize: 100), f.User.Id, _token);
        InUseRecord left = page.Items.Single(r => r.Source.Id == "900001");
        InUseRecord stays = page.Items.Single(r => r.Source.Id == "900002");
        // The next discovery returns only one of them: the other left the source group.
        InUseBatch onlyStays = new([stays.Source], false, "SourceCompletenessUnverified");
        f.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(onlyStays);
        return (f, left, stays);
    }

    private static async Task<InUseRefreshState> RefreshAgainAsync(Fixture f) =>
        (await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token)).Value!;

    [Fact]
    public async Task Recheck_RecordThatLeftTheGroup_DropsFromReview_AndStaysVisibleAsTracked()
    {
        (Fixture f, InUseRecord left, InUseRecord stays) = await TwoKnownAsync();
        f.Source.RecheckAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new InUseRecheckResult(new Dictionary<string, InUseEvidencePatch> { ["900001"] = LeftTeam() }, 0));

        InUseRefreshState state = await RefreshAgainAsync(f);

        await f.Source.Received(1).RecheckAsync(Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "900001" })), Arg.Any<CancellationToken>());
        state.RecheckUnchecked.Should().Be(0);
        (await f.Repository.QueryAsync(new(View: "review"), f.User.Id, _token)).Items.Select(r => r.Id).Should().Equal(stays.Id);
        InUseRecord tracked = (await f.Repository.QueryAsync(new(View: "tracking"), f.User.Id, _token)).Items.Single();
        tracked.Id.Should().Be(left.Id);
        tracked.ActivityStatus.Should().Be("NotAssigned");
        tracked.SourceObservationMissing.Should().BeTrue("the record was not observed by discovery; only the re-check evidence is applied");
    }

    [Fact]
    public async Task Recheck_NotProven_KeepsTheRecordListed_AndReportsTheUncheckedCount()
    {
        (Fixture f, InUseRecord left, _) = await TwoKnownAsync();
        f.Source.RecheckAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new InUseRecheckResult(new Dictionary<string, InUseEvidencePatch>(), 1));

        InUseRefreshState state = await RefreshAgainAsync(f);

        state.RecheckUnchecked.Should().Be(1);
        (await f.Repository.QueryAsync(new(View: "review", PageSize: 100), f.User.Id, _token)).Items.Select(r => r.Id).Should().Contain(left.Id);
        (await f.Repository.QueryAsync(new(View: "tracking"), f.User.Id, _token)).Total.Should().Be(0);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(InvalidDataException))]
    [InlineData(typeof(IOException))]
    public async Task Recheck_SourceFailure_DoesNotFailTheRefresh_AndKeepsTheRecordListed(Type failure)
    {
        (Fixture f, InUseRecord left, _) = await TwoKnownAsync();
        f.Source.RecheckAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns<Task<InUseRecheckResult>>(_ => throw (Exception)Activator.CreateInstance(failure)!);

        InUseRefreshState state = await RefreshAgainAsync(f);

        state.RecheckUnchecked.Should().Be(1);
        (await f.Repository.QueryAsync(new(View: "review", PageSize: 100), f.User.Id, _token)).Items.Select(r => r.Id).Should().Contain(left.Id);
    }

    [Fact]
    public async Task Recheck_MoreKnownRecordsThanTheBound_ChecksTheBoundAndCountsTheRest()
    {
        var f = new Fixture();
        InUseSource template = (await new LocalInUseSourceClient().DiscoverAsync(_token)).Records[1];
        InUseSource[] many = [.. Enumerable.Range(1, InUseService.MaxRecheck + 10).Select(i => template with { Id = (910000 + i).ToString(), Code = "OR-SYNTH-" + i })];
        f.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(new InUseBatch(many, false, "SourceCompletenessUnverified"));
        (await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token)).Error.Should().BeNull();
        f.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(new InUseBatch([], false, "SourceCompletenessUnverified"));
        f.Source.RecheckAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new InUseRecheckResult(new Dictionary<string, InUseEvidencePatch>(), 0));

        InUseRefreshState state = await RefreshAgainAsync(f);

        await f.Source.Received(1).RecheckAsync(Arg.Is<IReadOnlyList<string>>(ids => ids.Count == InUseService.MaxRecheck), Arg.Any<CancellationToken>());
        state.RecheckUnchecked.Should().Be(10);
    }

    [Fact]
    public async Task Recheck_AlreadyDroppedAndDiscoveredRecords_AreNotRequested()
    {
        (Fixture f, InUseRecord left, _) = await TwoKnownAsync();
        f.Source.RecheckAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new InUseRecheckResult(new Dictionary<string, InUseEvidencePatch> { ["900001"] = LeftTeam() }, 0));
        await RefreshAgainAsync(f);
        f.Source.ClearReceivedCalls();

        await RefreshAgainAsync(f);

        // 900002 was discovered and 900001 already left review: nothing is asked of the source.
        await f.Source.DidNotReceive().RecheckAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        (await f.Repository.QueryAsync(new(View: "tracking"), f.User.Id, _token)).Items.Single().Id.Should().Be(left.Id);
    }

    [Fact]
    public async Task Recheck_Client_ClassifiesByIdOneAtATime_AndCountsUnprovenRecords()
    {
        TrackingClassifiedInUseSourceClient client = new(Substitute.For<IInUseSourceClient>(), new FakeItsmRecordTracking());

        InUseRecheckResult result = await client.RecheckAsync(["900101", "424242", "900001", "900003"], _token);

        result.Patches.Keys.Should().BeEquivalentTo("900101", "900001");
        result.Patches["900101"].CurrentStage!.Value.Should().Be("NotAssignedToWasas");
        result.Patches["900101"].WasasActivity!.Value.Should().Be("Completed");
        result.Patches["900001"].CurrentStage!.Value.Should().Be("AssignedToWasas");
        result.Unchecked.Should().Be(2, "an unknown or unreadable record is unchecked, never absent");
    }

    [Fact]
    public async Task Recheck_ProvidersWithoutASourceRead_AskNothingAndCountNothing()
    {
        InUseRecheckResult local = await ((IInUseSourceClient)new LocalInUseSourceClient()).RecheckAsync(["900001"], _token);
        InUseRecheckResult disabled = await ((IInUseSourceClient)new DisabledInUseSourceClient()).RecheckAsync(["900001"], _token);

        new[] { local, disabled }.Should().OnlyContain(r => r.Patches.Count == 0 && r.Unchecked == 0);
    }
}
