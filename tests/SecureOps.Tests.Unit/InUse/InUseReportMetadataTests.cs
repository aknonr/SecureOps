using FluentAssertions;
using SecureOps.Infrastructure.InUse;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Unit.InUse;

public sealed class InUseReportMetadataTests
{
    [Fact]
    public void Projection_UsesOnlyOwnHistoricalProvenance_AndPreservesBytes()
    {
        var report = new InUseReport(Guid.NewGuid(), 13, 3, new string('A', 64), "old-guid.xlsx", [1, 2],
            [new("Provenance", [["Code", "OR-00123"]]), new("Sunucular", [["HOSTNAME", "001-server", "001-server"]])])
        { PreparedBy = Guid.NewGuid(), PreparedByLabel = "\u0130nceleyen", PreparedAt = DateTimeOffset.Parse("2026-09-01T00:00:00Z") };
        var metadata = InUseReportMetadata.From(report);
        metadata.SourceCode.Should().Be("OR-00123");
        metadata.HostsJson.Should().Be("[\"001-server\"]");
        metadata.PreparedByAccount.Should().BeNull();
        metadata.DownloadName.Should().Contain("OR-00123").And.Contain("20260901_000000Z_v13.xlsx");
        InUseReportMetadata.From(report).Should().Be(metadata);
        report.Content.Should().Equal(1, 2);
        report.FileName.Should().Be("old-guid.xlsx");
    }
}
