namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>Service Accounts module configuration. Disabled by default; no live integration is configured here.</summary>
public sealed class ServiceAccountOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "ServiceAccounts";

    /// <summary>"Disabled" (default) or "SqlServer" (requires the reviewed svcacct candidate schema).</summary>
    public string Provider { get; set; } = "Disabled";

    /// <summary>Maximum import upload size in bytes.</summary>
    public int MaxImportBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>Maximum evidence file size in bytes.</summary>
    public int MaxEvidenceBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Maximum usage-scan upload size in bytes (ADR-0027).</summary>
    public int MaxUsageScanBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>Reminder rules (explicit calendar days only; business days need an approved holiday calendar).</summary>
    public ServiceAccountReminderOptions Reminders { get; set; } = new();

    /// <summary>Risk list: no logon for at least this many calendar days (measured from the source report date).</summary>
    public int RiskLogonDays { get; set; } = SecureOps.Domain.ServiceAccounts.RiskThresholds.DefaultLogonDays;

    /// <summary>Risk list: password unchanged for at least this many calendar days (measured from the source report date).</summary>
    public int RiskPasswordDays { get; set; } = SecureOps.Domain.ServiceAccounts.RiskThresholds.DefaultPasswordDays;

    /// <summary>OR opening from an approved gMSA plan (ADR-0030, PROPOSED); the provider itself is <c>ItsmRequestCreation:Provider</c>.</summary>
    public ServiceAccountOperationalRecordOptions OperationalRecords { get; set; } = new();

    /// <summary>True when SQL persistence is configured.</summary>
    public bool Enabled => string.Equals(Provider, "SqlServer", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Reminder configuration. Outbound mail is not implemented; channels are in-app and coordinator draft.</summary>
public sealed class ServiceAccountReminderOptions
{
    /// <summary>Whether the Worker schedules the evaluator through the existing Hangfire server.</summary>
    public bool Enabled { get; set; }

    /// <summary>Cron for the recurring evaluator (UTC).</summary>
    public string Cron { get; set; } = "15 5 * * 1-5";

    /// <summary>Remind this many calendar days before a plan end; null disables the rule.</summary>
    public int? PlanEndLeadDays { get; set; } = 2;

    /// <summary>Remind when no reply has been recorded this many calendar days after first send; null disables.</summary>
    public int? NoReplyAfterDays { get; set; } = 7;

    /// <summary>Maximum delivery attempts before dead-letter.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Lease length in seconds for multi-instance claim.</summary>
    public int LeaseSeconds { get; set; } = 120;

    /// <summary>Approved holiday calendar identifier; empty keeps business-day rules unconfigured.</summary>
    public string? HolidayCalendar { get; set; }
}

/// <summary>
/// Module-side OR settings. Placeholder environment values until the Turuncu Hat team confirms the template property values
/// (ADR-0030 open question 4); the short description limit until its real maximum is known (open question 7).
/// </summary>
public sealed class ServiceAccountOperationalRecordOptions
{
    /// <summary>Environment values the person may choose (an array, so configuration replaces rather than appends).</summary>
    public string[] Environments { get; set; } = ["Test", "Prod"];

    /// <summary>Short description limit (at most the 400-character column).</summary>
    public int ShortDescriptionMax { get; set; } = 200;
}
