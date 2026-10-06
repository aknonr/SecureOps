using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Tests.Unit.ServiceAccounts;

/// <summary>Per-account outcome of attaching one usage-scan file to several accounts (ADR-0027).</summary>
public sealed class UsageScanBatchTests
{
    [Theory]
    [InlineData(false, false, "SYN\\svc_a", false, UsageScanBatchOutcome.Unavailable)]
    [InlineData(false, true, "SYN\\svc_a", false, UsageScanBatchOutcome.Unavailable)]
    [InlineData(true, false, "SYN\\svc_a", false, UsageScanBatchOutcome.Unavailable)]
    [InlineData(true, false, null, true, UsageScanBatchOutcome.Unavailable)]
    [InlineData(true, true, "SYN\\svc_a", false, UsageScanBatchOutcome.Attached)]
    [InlineData(true, true, null, true, UsageScanBatchOutcome.Ambiguous)]
    [InlineData(true, true, null, false, UsageScanBatchOutcome.NotInScan)]
    public void Precheck_LooksAtScopeAndBasisBeforeTheFile(bool visible, bool responsible, string? matched, bool ambiguous, UsageScanBatchOutcome expected) =>
        UsageScanBatch.Precheck(visible, responsible, matched, ambiguous).Should().Be(expected,
            "a file never tells the caller anything about an account it may not work on");

    [Fact]
    public void Labels_AreTurkish_DistinctAndNeverSayNotUsed()
    {
        string[] labels = [.. Enum.GetValues<UsageScanBatchOutcome>().Select(UsageScanBatch.Label)];

        labels.Should().OnlyHaveUniqueItems().And.NotContain(l => l.Contains("kullanılmıyor", StringComparison.OrdinalIgnoreCase));
        UsageScanBatch.Label(UsageScanBatchOutcome.Unavailable).Should().Contain("Bulunamadı").And.Contain("yetki");
        UsageScanBatch.Label(UsageScanBatchOutcome.Failed).Should().Contain("tekrarlamak güvenlidir");
        UsageScanBatch.MaxAccounts.Should().Be(20);
    }
}
