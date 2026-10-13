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

    /// <summary>Web usage scan jobs (USAGE-SCAN-JOB-CONTRACT.md); off unless a collector is configured.</summary>
    public ServiceAccountUsageScanJobOptions UsageScanJobs { get; set; } = new();

    /// <summary>Risk list: no logon for at least this many calendar days (measured from the source report date).</summary>
    public int RiskLogonDays { get; set; } = SecureOps.Domain.ServiceAccounts.RiskThresholds.DefaultLogonDays;

    /// <summary>Risk list: password unchanged for at least this many calendar days (measured from the source report date).</summary>
    public int RiskPasswordDays { get; set; } = SecureOps.Domain.ServiceAccounts.RiskThresholds.DefaultPasswordDays;

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
/// Web usage scan jobs (ADR-0024 R2-4, USAGE-SCAN-JOB-CONTRACT.md). <see cref="Collector"/> defaults to <c>None</c>: the feature is
/// off and says so. <c>Fake</c> is accepted only in Development, Demo and Test environments; <c>Jea</c> needs the Worker's JEA runner.
/// </summary>
public sealed class ServiceAccountUsageScanJobOptions
{
    /// <summary><c>None</c> (default), <c>Fake</c> or <c>Jea</c>.</summary>
    public string Collector { get; set; } = "None";

    /// <summary>Most servers read at once (1–64, default 32).</summary>
    public int MaxParallel { get; set; } = 32;

    /// <summary>Connection open timeout per server in seconds (default 15).</summary>
    public int OpenTimeoutSeconds { get; set; } = 15;

    /// <summary>Whole-call timeout per server in seconds (default 180).</summary>
    public int OperationTimeoutSeconds { get; set; } = 180;

    /// <summary>Largest document accepted per server in bytes (default 512 KiB, at most 1 MiB).</summary>
    public int MaxDocumentBytes { get; set; } = 512 * 1024;

    /// <summary>Worker lease in seconds, renewed every few seconds; an expired lease frees the accounts (default 300).</summary>
    public int LeaseSeconds { get; set; } = 300;

    /// <summary>A job nobody claimed within this many minutes is closed as not started (default 15).</summary>
    public int QueueTimeoutMinutes { get; set; } = 15;

    /// <summary>Fake collector only: base delay per server for the local demo (0 = none).</summary>
    public int FakeDelayMilliseconds { get; set; }

    /// <summary>Bounds handed to the collector, clamped to safe ranges.</summary>
    public UsageScans.Collection.UsageCollectionLimits Limits => new(Math.Clamp(MaxParallel, 1, 64),
        TimeSpan.FromSeconds(Math.Clamp(OpenTimeoutSeconds, 1, 120)), TimeSpan.FromSeconds(Math.Clamp(OperationTimeoutSeconds, 5, 900)),
        Math.Clamp(MaxDocumentBytes, 1024, 1024 * 1024));
}
