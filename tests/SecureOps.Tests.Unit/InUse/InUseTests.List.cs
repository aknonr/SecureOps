using FluentAssertions;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Unit.InUse;

public sealed partial class InUseTests
{
    [Theory]
    [InlineData("Pending", "fixture", false, false, "Pending")]
    [InlineData("Pending", "fixture", true, false, "VerificationPending")]
    [InlineData("Pending", "fixture", false, true, "VerificationPending")]
    [InlineData("Pending", "", false, false, "VerificationPending")]
    [InlineData("Completed", "fixture", true, true, "Completed")]
    [InlineData("Completed", " ", false, false, "VerificationPending")]
    [InlineData("Unknown", "fixture", false, false, "VerificationPending")]
    [InlineData(null, "fixture", false, false, "VerificationPending")]
    public async Task MinimalActivity_RequiresSourceEvidence(string? value, string provenance, bool active, bool missing, string expected)
    {
        var f = new Fixture();
        InUseRecord record = await f.ImportAsync();
        record = record with
        {
            Source = record.Source with { WasasActivity = new(value, provenance), Lifecycle = null },
            HasActiveExecution = active,
            SourceObservationMissing = missing,
            Completion = new(Guid.NewGuid(), f.User.Id, 1, 1, "fixture", DateTimeOffset.UtcNow, "ManuallyConfirmed")
        };
        record.ActivityStatus.Should().Be(expected);
        record.TrackingOnly.Should().Be(expected == "Completed");
        (record with { ActivityVerifiedAt = DateTimeOffset.UtcNow }).ActivityStatus.Should().Be("Completed");
        (record with { Source = record.Source with { WasasActivity = null, Lifecycle = new("Closed", "fixture") } }).ActivityStatus.Should().Be("OrClosed");
    }

    [Theory]
    [InlineData("oldest", "pending", null)]
    [InlineData("code", "pending", null)]
    [InlineData("newest", "verification", null)]
    [InlineData("invalid", "all", "InUseInvalid")]
    [InlineData("oldest", "invalid", "InUseInvalid")]
    public async Task ListQuery_ValidatesOrderingAndMinimalViews(string sort, string view, string? error)
    {
        var f = new Fixture();
        (await f.Service.QueryAsync(_principal, _context, new(View: view, Sort: sort), _token)).Error.Should().Be(error);
    }
}
