using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Infrastructure.Reporting;

/// <summary>Validated half-open UTC interval used by reporting queries.</summary>
public sealed record ReportingWindow(
    string Selection,
    DateTimeOffset FromInclusiveUtc,
    DateTimeOffset ToExclusiveUtc)
{
    /// <summary>Whether an instant falls in the half-open window [from, to).</summary>
    public bool Contains(DateTimeOffset instant) => instant >= FromInclusiveUtc && instant < ToExclusiveUtc;
}

/// <summary>Result of deterministic report-window validation.</summary>
public sealed record ReportingWindowResolution(ReportingWindow? Window, string? ErrorCode)
{
    /// <summary>Whether validation succeeded.</summary>
    public bool IsValid => Window is not null;
}

/// <summary>Resolves named or custom report windows without server-local timezone behavior.</summary>
public sealed class ReportingWindowResolver
{
    /// <summary>Maximum custom interval.</summary>
    public const int MaximumCustomDays = 92;

    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the resolver.</summary>
    public ReportingWindowResolver(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>Resolves today, 7d, 30d, or a bounded custom UTC interval.</summary>
    public ReportingWindowResolution Resolve(
        string? selection,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();
        string normalized = string.IsNullOrWhiteSpace(selection) ? "7d" : selection.Trim().ToLowerInvariant();

        if (normalized == "custom")
        {
            if (from is null || to is null)
            {
                return Invalid();
            }

            DateTimeOffset fromUtc = from.Value.ToUniversalTime();
            DateTimeOffset toUtc = to.Value.ToUniversalTime();
            if (fromUtc >= toUtc || toUtc > now || toUtc - fromUtc > TimeSpan.FromDays(MaximumCustomDays))
            {
                return Invalid();
            }

            return Valid("custom", fromUtc, toUtc);
        }

        if (from is not null || to is not null)
        {
            return Invalid();
        }

        return normalized switch
        {
            "today" => Valid("today", new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero), now),
            "7d" => Valid("7d", now.AddDays(-7), now),
            "30d" => Valid("30d", now.AddDays(-30), now),
            _ => Invalid()
        };
    }

    private static ReportingWindowResolution Valid(
        string selection,
        DateTimeOffset from,
        DateTimeOffset to) =>
        new(new ReportingWindow(selection, from, to), null);

    private static ReportingWindowResolution Invalid() => new(null, OperationalErrorCodes.ReportingValidationFailed);
}
