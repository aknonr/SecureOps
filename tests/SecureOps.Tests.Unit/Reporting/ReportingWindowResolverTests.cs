using FluentAssertions;
using SecureOps.Infrastructure.Reporting;

namespace SecureOps.Tests.Unit.Reporting;

public sealed class ReportingWindowResolverTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 23, 12, 30, 0, TimeSpan.Zero);
    private readonly ReportingWindowResolver _resolver = new(new FixedTimeProvider(_now));

    [Fact]
    public void Resolve_Today_UsesUtcMidnightAndCurrentInstant()
    {
        ReportingWindowResolution result = _resolver.Resolve("today", null, null);

        result.IsValid.Should().BeTrue();
        result.Window!.FromInclusiveUtc.Should().Be(new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero));
        result.Window.ToExclusiveUtc.Should().Be(_now);
    }

    [Theory]
    [InlineData(null, 7)]
    [InlineData("7d", 7)]
    [InlineData("30d", 30)]
    public void Resolve_RollingPreset_UsesExactUtcDuration(string? selection, int days)
    {
        ReportingWindow window = _resolver.Resolve(selection, null, null).Window!;

        window.FromInclusiveUtc.Should().Be(_now.AddDays(-days));
        window.ToExclusiveUtc.Should().Be(_now);
    }

    [Fact]
    public void Resolve_Custom_AllowsExactlyMaximumBoundedRange()
    {
        ReportingWindowResolution result = _resolver.Resolve("custom", _now.AddDays(-92), _now);

        result.IsValid.Should().BeTrue();
        result.Window!.Selection.Should().Be("custom");
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("custom")]
    public void Resolve_InvalidSelectionOrMissingCustomBounds_IsRejected(string selection)
    {
        _resolver.Resolve(selection, null, null).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Resolve_Custom_RejectsOversizeFutureAndEmptyIntervals()
    {
        _resolver.Resolve("custom", _now.AddDays(-92).AddTicks(-1), _now).IsValid.Should().BeFalse();
        _resolver.Resolve("custom", _now.AddDays(-1), _now.AddTicks(1)).IsValid.Should().BeFalse();
        _resolver.Resolve("custom", _now, _now).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Resolve_PresetWithCustomBounds_IsRejectedAsAmbiguous()
    {
        _resolver.Resolve("7d", _now.AddDays(-1), _now).IsValid.Should().BeFalse();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
