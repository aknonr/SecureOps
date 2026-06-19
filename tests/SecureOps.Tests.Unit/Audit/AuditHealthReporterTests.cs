using FluentAssertions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Audit;

namespace SecureOps.Tests.Unit.Audit;

public sealed class AuditHealthReporterTests
{
    [Fact]
    public void GetHealth_WhenStoreMarkedUnhealthy_ReturnsUnhealthyStatus()
    {
        AuditStoreHealthState healthState = new();
        healthState.MarkUnhealthy("AuditSinkUnavailable");
        AuditHealthReporter reporter = new(
            Options.Create(new AuditOptions { Provider = "File", FailClosed = true }),
            new NullAuditQueueMetrics(),
            healthState);

        AuditStoreHealthResponse response = reporter.GetHealth();

        response.Status.Should().Be("Unhealthy");
        response.LastErrorCode.Should().Be("AuditSinkUnavailable");
        response.Persistent.Should().BeTrue();
    }
}
