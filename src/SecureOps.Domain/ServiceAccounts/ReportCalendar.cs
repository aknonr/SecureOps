namespace SecureOps.Domain.ServiceAccounts;

/// <summary>Where an event falls relative to a report week and cut-off.</summary>
public enum WeekPlacement
{
    /// <summary>Inside [Monday, next Monday) and not after the cut-off.</summary>
    InPeriod,
    /// <summary>Before the report week.</summary>
    Earlier,
    /// <summary>After the week but not after the cut-off.</summary>
    LaterBeforeCutoff,
    /// <summary>After the report cut-off; excluded from this report.</summary>
    AfterCutoff,
    /// <summary>No real date; never placed into a week by guess.</summary>
    UnknownDate
}

/// <summary>
/// Report business calendar. Weeks are Europe/Istanbul [Monday 00:00, next Monday 00:00).
/// Date-only events keep their business date; instants are converted to the Istanbul local date.
/// Only calendar days are used; business days require an approved holiday calendar.
/// </summary>
public static class ReportCalendar
{
    private static readonly Lazy<TimeZoneInfo> _istanbul = new(() =>
    {
        foreach (string id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out TimeZoneInfo? zone))
            {
                return zone;
            }
        }

        throw new InvalidOperationException("The Europe/Istanbul time zone is unavailable on this host.");
    });

    /// <summary>Europe/Istanbul time zone.</summary>
    public static TimeZoneInfo Istanbul => _istanbul.Value;

    /// <summary>Monday of the week containing the date.</summary>
    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>Istanbul business date of an instant.</summary>
    public static DateOnly LocalDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Istanbul).DateTime);

    /// <summary>Istanbul local midnight of a business date as an instant.</summary>
    public static DateTimeOffset StartOf(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Istanbul.GetUtcOffset(local));
    }

    /// <summary>
    /// Places an event relative to the week starting <paramref name="weekStart"/> and the cut-off.
    /// Instants are compared exactly with the cut-off; date-only values by business date.
    /// </summary>
    public static WeekPlacement Place(TimePrecision precision, DateOnly? date, DateTimeOffset? instant, DateOnly weekStart, DateTimeOffset asOf) =>
        Place(precision, date, instant, weekStart, weekStart.AddDays(7), asOf);

    /// <summary>Places an event relative to the business-date period [<paramref name="start"/>, <paramref name="endExclusive"/>) and the cut-off.</summary>
    public static WeekPlacement Place(TimePrecision precision, DateOnly? date, DateTimeOffset? instant, DateOnly start, DateOnly endExclusive, DateTimeOffset asOf)
    {
        DateOnly weekStart = start;
        if (precision == TimePrecision.Unknown || date is null)
        {
            return WeekPlacement.UnknownDate;
        }

        DateOnly local = precision == TimePrecision.Instant && instant is { } at ? LocalDate(at) : date.Value;
        bool afterCutoff = precision == TimePrecision.Instant && instant is { } exact
            ? exact > asOf
            : local > LocalDate(asOf);
        if (afterCutoff)
        {
            return WeekPlacement.AfterCutoff;
        }

        if (local < weekStart)
        {
            return WeekPlacement.Earlier;
        }

        return local < endExclusive ? WeekPlacement.InPeriod : WeekPlacement.LaterBeforeCutoff;
    }
}
