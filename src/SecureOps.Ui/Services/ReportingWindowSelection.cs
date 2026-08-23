namespace SecureOps.Ui.Services;

/// <summary>
/// The report windows the UI offers, matching the presets the reporting API accepts.
/// </summary>
public enum ReportingWindowPreset
{
    /// <summary>The current UTC day so far.</summary>
    Today,

    /// <summary>Rolling seven days ending now.</summary>
    Last7Days,

    /// <summary>Rolling thirty days ending now.</summary>
    Last30Days,

    /// <summary>An operator-chosen bounded interval.</summary>
    Custom
}

/// <summary>
/// A window the UI is prepared to send, or the reason it will not send it.
/// </summary>
/// <param name="Selection">Wire value for the <c>window</c> query parameter.</param>
/// <param name="FromInclusiveUtc">Custom lower bound; <c>null</c> for presets.</param>
/// <param name="ToExclusiveUtc">Custom upper bound; <c>null</c> for presets.</param>
/// <param name="Error">Operator-facing reason the range was refused, or <c>null</c> when valid.</param>
public sealed record ReportingWindowRequest(
    string Selection,
    DateTimeOffset? FromInclusiveUtc,
    DateTimeOffset? ToExclusiveUtc,
    string? Error)
{
    /// <summary>Whether this window can be sent to the API.</summary>
    public bool IsValid => Error is null;
}

/// <summary>
/// Builds report-window requests and refuses ranges the API would reject.
/// </summary>
/// <remarks>
/// <para>
/// The rules here deliberately mirror <c>ReportingWindowResolver</c> on the server: half-open UTC
/// intervals, presets that carry no explicit bounds, and a custom range capped at
/// <see cref="MaximumCustomDays"/> days. The server stays the authority — it re-validates everything
/// and returns <c>ReportingValidationFailed</c> — but a client that knows the same rules can explain
/// the problem next to the date fields instead of turning a correctable mistake into a round-trip
/// and a generic error panel.
/// </para>
/// <para>
/// Dates are interpreted as UTC calendar days, not local ones. The backend aggregates on UTC day
/// boundaries, so treating a picked date as local would silently shift every bucket by the offset
/// and make the operator's range disagree with the numbers inside it.
/// </para>
/// </remarks>
public static class ReportingWindowSelection
{
    /// <summary>Longest custom interval the API accepts.</summary>
    /// <remarks>Must stay equal to <c>ReportingWindowResolver.MaximumCustomDays</c>.</remarks>
    public const int MaximumCustomDays = 92;

    /// <summary>Wire value for the current UTC day.</summary>
    public const string TodaySelection = "today";

    /// <summary>Wire value for the rolling seven-day window.</summary>
    public const string Last7DaysSelection = "7d";

    /// <summary>Wire value for the rolling thirty-day window.</summary>
    public const string Last30DaysSelection = "30d";

    /// <summary>Wire value for an explicit interval.</summary>
    public const string CustomSelection = "custom";

    /// <summary>
    /// Builds the request for a preset window.
    /// </summary>
    /// <param name="preset">Chosen preset. <see cref="ReportingWindowPreset.Custom"/> is not valid here.</param>
    /// <returns>A window request carrying only the selection.</returns>
    /// <remarks>
    /// Presets send no bounds at all. The server rejects a preset that arrives with <c>from</c> or
    /// <c>to</c>, so leaving a previously entered custom range attached would break the request.
    /// </remarks>
    public static ReportingWindowRequest ForPreset(ReportingWindowPreset preset) => preset switch
    {
        ReportingWindowPreset.Today => new ReportingWindowRequest(TodaySelection, null, null, null),
        ReportingWindowPreset.Last7Days => new ReportingWindowRequest(Last7DaysSelection, null, null, null),
        ReportingWindowPreset.Last30Days => new ReportingWindowRequest(Last30DaysSelection, null, null, null),
        _ => new ReportingWindowRequest(
            CustomSelection,
            null,
            null,
            "Özel aralık için başlangıç ve bitiş tarihi seçin.")
    };

    /// <summary>
    /// Builds and validates a custom window from two picked calendar dates.
    /// </summary>
    /// <param name="fromDate">First day to include, read as a UTC calendar day.</param>
    /// <param name="toDate">Last day to include, read as a UTC calendar day.</param>
    /// <param name="utcNow">Current UTC time, used to refuse future ranges.</param>
    /// <returns>A sendable request, or one carrying the reason it was refused.</returns>
    /// <remarks>
    /// <paramref name="toDate"/> is inclusive for the operator and exclusive on the wire: picking
    /// 1 March to 3 March asks for three whole days, so the upper bound becomes 4 March 00:00Z. When
    /// that bound is still in the future it is clamped to <paramref name="utcNow"/>, because the API
    /// refuses a window that ends after the present and an unclamped "today" would always fail.
    /// </remarks>
    public static ReportingWindowRequest ForCustom(
        DateTime? fromDate,
        DateTime? toDate,
        DateTimeOffset utcNow)
    {
        if (fromDate is null || toDate is null)
        {
            return Refused("Özel aralık için başlangıç ve bitiş tarihi seçin.");
        }

        DateTime fromDay = fromDate.Value.Date;
        DateTime toDay = toDate.Value.Date;

        if (toDay < fromDay)
        {
            return Refused("Bitiş tarihi başlangıç tarihinden önce olamaz.");
        }

        var fromInclusive = new DateTimeOffset(DateTime.SpecifyKind(fromDay, DateTimeKind.Utc));
        var toExclusive = new DateTimeOffset(DateTime.SpecifyKind(toDay.AddDays(1), DateTimeKind.Utc));

        DateTimeOffset nowUtc = utcNow.ToUniversalTime();
        if (fromInclusive >= nowUtc)
        {
            return Refused("Başlangıç tarihi gelecekte olamaz.");
        }

        // The operator's last day is usually the current day, whose exclusive bound has not arrived
        // yet. Reporting to "now" is what they meant, and it is what the server will accept.
        if (toExclusive > nowUtc)
        {
            toExclusive = nowUtc;
        }

        if (toExclusive - fromInclusive > TimeSpan.FromDays(MaximumCustomDays))
        {
            return Refused($"Özel aralık en fazla {MaximumCustomDays} gün olabilir.");
        }

        return new ReportingWindowRequest(CustomSelection, fromInclusive, toExclusive, null);
    }

    /// <summary>
    /// Turkish label for a preset.
    /// </summary>
    /// <param name="preset">Preset to describe.</param>
    /// <returns>Short label for the window control.</returns>
    public static string Label(ReportingWindowPreset preset) => preset switch
    {
        ReportingWindowPreset.Today => "Bugün",
        ReportingWindowPreset.Last7Days => "Son 7 gün",
        ReportingWindowPreset.Last30Days => "Son 30 gün",
        _ => "Özel aralık"
    };

    private static ReportingWindowRequest Refused(string error) =>
        new(CustomSelection, null, null, error);
}
