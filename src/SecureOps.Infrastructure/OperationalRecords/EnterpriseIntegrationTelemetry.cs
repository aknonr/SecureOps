using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Secret-free dependency and workflow metric instruments.</summary>
public sealed class EnterpriseIntegrationTelemetry
{
    private static readonly Meter _meter = new("SecureOps.EnterpriseIntegrations", "1.0.0");
    private readonly Counter<long> _operations = _meter.CreateCounter<long>("secureops.integration.operations");
    private readonly Counter<long> _records = _meter.CreateCounter<long>("secureops.integration.records");
    private readonly Counter<long> _malformed = _meter.CreateCounter<long>("secureops.integration.malformed_records");
    private readonly Histogram<double> _duration = _meter.CreateHistogram<double>("secureops.integration.duration", "ms");

    /// <summary>Records one dependency operation without remote identifiers.</summary>
    public void RecordOperation(string provider, string operation, string outcome, TimeSpan duration)
    {
        TagList tags = new()
        {
            { "provider", provider },
            { "operation", operation },
            { "outcome", outcome }
        };
        _operations.Add(1, tags);
        _duration.Record(duration.TotalMilliseconds, tags);
    }

    /// <summary>Records bounded aggregate record counts.</summary>
    public void RecordRecords(string provider, string operation, int count, int malformed)
    {
        TagList tags = new() { { "provider", provider }, { "operation", operation } };
        _records.Add(count, tags);
        if (malformed > 0)
        {
            _malformed.Add(malformed, tags);
        }
    }
}
