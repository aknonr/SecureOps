using FluentAssertions;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins the client-side window rules against the server's.
/// </summary>
/// <remarks>
/// These duplicate <c>ReportingWindowResolver</c> deliberately, so a divergence shows up as a failed
/// test rather than as a report the operator cannot explain. If the server's rules change, both this
/// file and <see cref="ReportingWindowSelection"/> have to move with them.
/// </remarks>
public sealed class ReportingWindowSelectionTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 23, 14, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ReportingWindowPreset.Today, "today")]
    [InlineData(ReportingWindowPreset.Last7Days, "7d")]
    [InlineData(ReportingWindowPreset.Last30Days, "30d")]
    public void Preset_SendsSelectionWithoutBounds(ReportingWindowPreset preset, string expected)
    {
        // The API rejects a preset that arrives carrying from/to, so bounds left over from a
        // previous custom range would turn a working preset into a validation failure.
        ReportingWindowRequest request = ReportingWindowSelection.ForPreset(preset);

        request.IsValid.Should().BeTrue();
        request.Selection.Should().Be(expected);
        request.FromInclusiveUtc.Should().BeNull();
        request.ToExclusiveUtc.Should().BeNull();
    }

    [Fact]
    public void CustomPreset_WithoutDates_IsRefused()
    {
        ReportingWindowRequest request = ReportingWindowSelection.ForPreset(ReportingWindowPreset.Custom);

        request.IsValid.Should().BeFalse();
        request.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Custom_TreatsTheEndDayAsIncluded()
    {
        // An operator picking 1-3 March means three whole days. The wire bound is exclusive, so the
        // upper bound has to land on the 4th or the last day silently drops out of the report.
        ReportingWindowRequest request = ReportingWindowSelection.ForCustom(
            new DateTime(2026, 3, 1),
            new DateTime(2026, 3, 3),
            _now);

        request.IsValid.Should().BeTrue();
        request.Selection.Should().Be("custom");
        request.FromInclusiveUtc.Should().Be(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
        request.ToExclusiveUtc.Should().Be(new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Custom_ClampsAnEndBoundThatHasNotArrivedYet()
    {
        // Picking today is the common case, and its exclusive bound is tomorrow midnight. Sent
        // unclamped the API refuses the whole window, so "today" would never work.
        ReportingWindowRequest request = ReportingWindowSelection.ForCustom(
            new DateTime(2026, 8, 20),
            new DateTime(2026, 8, 23),
            _now);

        request.IsValid.Should().BeTrue();
        request.ToExclusiveUtc.Should().Be(_now);
    }

    [Fact]
    public void Custom_UsesUtcDaysRatherThanLocalOnes()
    {
        // The backend buckets on UTC days. A picked date read as local would shift every bucket by
        // the machine's offset and put the operator's range out of step with the numbers in it.
        ReportingWindowRequest request = ReportingWindowSelection.ForCustom(
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Local),
            new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Local),
            _now);

        request.FromInclusiveUtc!.Value.Offset.Should().Be(TimeSpan.Zero);
        request.FromInclusiveUtc!.Value.UtcDateTime.Should().Be(new DateTime(2026, 3, 1, 0, 0, 0));
    }

    [Fact]
    public void Custom_IgnoresTheTimeComponentOfAPickedDate()
    {
        ReportingWindowRequest request = ReportingWindowSelection.ForCustom(
            new DateTime(2026, 3, 1, 17, 42, 9),
            new DateTime(2026, 3, 2, 3, 15, 0),
            _now);

        request.FromInclusiveUtc!.Value.UtcDateTime.Should().Be(new DateTime(2026, 3, 1));
        request.ToExclusiveUtc!.Value.UtcDateTime.Should().Be(new DateTime(2026, 3, 3));
    }

    [Fact]
    public void Custom_RefusesAReversedRange()
    {
        ReportingWindowRequest request = ReportingWindowSelection.ForCustom(
            new DateTime(2026, 3, 10),
            new DateTime(2026, 3, 1),
            _now);

        request.IsValid.Should().BeFalse();
        request.Error.Should().Contain("Bitiş");
    }

    [Fact]
    public void Custom_RefusesAStartInTheFuture()
    {
        ReportingWindowRequest request = ReportingWindowSelection.ForCustom(
            new DateTime(2026, 9, 1),
            new DateTime(2026, 9, 2),
            _now);

        request.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Custom_AcceptsExactlyTheMaximumSpan()
    {
        // 1 May to 31 July inclusive is exactly 92 days once the end is made exclusive, which is
        // the largest window the API accepts. One day either side of this is the interesting case,
        // so the boundary is asserted rather than assumed.
        ReportingWindowRequest request = ReportingWindowSelection.ForCustom(
            new DateTime(2026, 5, 1),
            new DateTime(2026, 7, 31),
            _now);

        request.IsValid.Should().BeTrue();
        (request.ToExclusiveUtc!.Value - request.FromInclusiveUtc!.Value)
            .Should().Be(TimeSpan.FromDays(ReportingWindowSelection.MaximumCustomDays));
    }

    [Fact]
    public void Custom_RefusesARangeOneDayBeyondTheCap()
    {
        ReportingWindowRequest request = ReportingWindowSelection.ForCustom(
            new DateTime(2026, 4, 30),
            new DateTime(2026, 7, 31),
            _now);

        request.IsValid.Should().BeFalse();
        request.Error.Should().Contain(ReportingWindowSelection.MaximumCustomDays.ToString());
    }

    [Fact]
    public void MaximumCustomDays_MatchesTheServerCap()
    {
        // Guards the one number that has to agree with ReportingWindowResolver.MaximumCustomDays.
        ReportingWindowSelection.MaximumCustomDays.Should().Be(92);
    }

    [Theory]
    [InlineData(ReportingWindowPreset.Today)]
    [InlineData(ReportingWindowPreset.Last7Days)]
    [InlineData(ReportingWindowPreset.Last30Days)]
    [InlineData(ReportingWindowPreset.Custom)]
    public void EveryPreset_HasALabel(ReportingWindowPreset preset) =>
        ReportingWindowSelection.Label(preset).Should().NotBeNullOrWhiteSpace();
}
