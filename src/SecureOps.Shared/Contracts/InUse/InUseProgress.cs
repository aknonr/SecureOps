using System.Globalization;

namespace SecureOps.Shared.Contracts.InUse;

/// <summary>Local intent anchored to immutable evidence. External execution is not enabled.</summary>
public sealed record InUseCompletion(Guid CommandId, Guid ActorId, long ReportVersion, long SourceVersion,
    string ReportSha256, DateTimeOffset ConfirmedAt, string Stage = "Blocked",
    string? AttachmentId = null, string? TaskId = null, string? FinalOrState = null);

/// <summary>Explicit confirmation of the exact current archived workbook.</summary>
public sealed record ConfirmInUseRequest(long ExpectedVersion, Guid CommandId, string ReportSha256);

/// <summary>One row per stored OR; never one per server or owner.</summary>
public sealed record InUseOverview(int Total, int Open, int Unknown, int AwaitingAnswers, int ReportReady,
    IReadOnlyList<InUseWaitingRecord> Oldest, InUseRefreshState Refresh);

/// <summary>Lightweight oldest-record navigation without server inventories or review payloads.</summary>
public sealed record InUseWaitingRecord(Guid Id, string Code, DateTimeOffset CreatedAt);

/// <summary>Deterministic source age/readiness semantics shared by reporting and review.</summary>
public static class InUseProgress
{
    /// <summary>Accepts only explicit, offset-bearing parent creation evidence. Future/invalid dates stay unknown.</summary>
    public static DateTimeOffset? Created(InUseSource source, DateTimeOffset now) =>
        source.Creation is { Value: { } value, Source.Length: > 0 }
        && !string.IsNullOrWhiteSpace(source.Creation.Source)
        && (value.EndsWith('Z') || value.Length > 19 && (value[^6] is '+' or '-'))
        && DateTimeOffset.TryParseExact(value, ["O", "yyyy-MM-dd'T'HH:mm:ssK"], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset date) && date <= now ? date : null;
    /// <summary>Local workbook readiness, not global relationship completeness or corporate acceptance.</summary>
    public static bool Ready(InUseRecord r) => !r.Discarded && r.ReviewCurrent && r.Draft is not null
        && InUseChecks.RelationshipReady(r.Source) && InUseChecks.Missing(r.Source, r.Draft.Answers) is null;
    /// <summary>Aggregates stored records once; callers supply a consistent snapshot.</summary>
    public static InUseOverview Summarize(IReadOnlyList<InUseRecord> records, InUseRefreshState refresh, DateTimeOffset now) =>
        new(records.Count(r => !r.Discarded), records.Count(r => !r.Discarded && r.Source.Lifecycle is { Value: "Open", Source.Length: > 0 }),
            records.Count(r => !r.Discarded && r.Source.Lifecycle is not { Value: "Open" or "Closed", Source.Length: > 0 }),
            records.Count(r => !r.Discarded && !Ready(r)), records.Count(Ready),
            records.Where(r => !r.Discarded && r.Source.Lifecycle is { Value: "Open", Source.Length: > 0 } && Created(r.Source, now) is not null)
                .OrderBy(r => Created(r.Source, now)).ThenBy(r => r.Id).Take(5)
                .Select(r => new InUseWaitingRecord(r.Id, r.Source.Code, Created(r.Source, now)!.Value)).ToArray(), refresh);
}
