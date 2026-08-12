using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.OperationalRecords;

namespace SecureOps.Tests.Unit.OperationalRecords;

internal static class TestRecord
{
    public static OperationalRecordSourceItem SourceItem(string sourceId = "source-100", string orCode = "OR-100") => new(
        sourceId,
        orCode,
        "Synthetic service request",
        "Synthetic operational description.",
        "sample.requester",
        new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero),
        "TEST",
        "server-placeholder",
        "application-placeholder");

    public static async Task<OperationalRecord> SeedEligibleAsync(InMemoryOperationalRecordRepository repository, CancellationToken cancellationToken = default)
    {
        OperationalRecord imported = await repository.UpsertImportedAsync(SourceItem(), "correlation-seed", cancellationToken);
        return await repository.SetClassificationAsync(
            imported.Id,
            new OperationalRecordClassificationResult(OperationalRecordClassification.OperationalSupport, true, "Approved test rule."),
            "correlation-seed",
            cancellationToken);
    }
}
