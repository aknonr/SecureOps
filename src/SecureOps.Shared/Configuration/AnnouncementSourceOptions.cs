namespace SecureOps.Shared.Configuration;

/// <summary>Server-owned maintenance-profile and source configuration, independent of Turuncu Hat credentials.</summary>
public sealed class AnnouncementSourceOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "AnnouncementSource";

    /// <summary>Default-off source activation, separate from draft module opt-in.</summary>
    public bool Enabled { get; set; }
    /// <summary>Collection membership provider: Disabled, Fixture or ConfigurationManager.</summary>
    public string CollectionProvider { get; set; } = "Disabled";
    /// <summary>Service/OCO provider: Disabled, Fixture or TuruncuHat.</summary>
    public string ServiceProvider { get; set; } = "Disabled";
    /// <summary>Local fixture directory for the Fixture providers; never a corporate path.</summary>
    public string FixtureDirectory { get; set; } = "";
    /// <summary>SCCM site code used by the read-only ConfigurationManager provider.</summary>
    public string SiteCode { get; set; } = "";
    /// <summary>SMS provider host for the read-only ConfigurationManager provider.</summary>
    public string ProviderMachineName { get; set; } = "";
    /// <summary>Hard device ceiling per retrieval; exceeding it yields an explicit partial result.</summary>
    public int MaxDevices { get; set; } = 2000;
    /// <summary>Devices returned per collection page.</summary>
    public int DevicePageSize { get; set; } = 500;
    /// <summary>Maximum concurrent device-to-service lookups.</summary>
    public int ServiceLookupConcurrency { get; set; } = 4;
    /// <summary>Whole-job wall clock budget; expiry produces Partial or Failed, never silent success.</summary>
    public int JobTimeoutSeconds { get; set; } = 600;
    /// <summary>Maximum source execution attempts before an abandoned job fails explicitly (1-5).</summary>
    public int MaxExecutionAttempts { get; set; } = 3;
    /// <summary>Per-device service lookup budget.</summary>
    public int DeviceLookupTimeoutSeconds { get; set; } = 30;
    /// <summary>Turuncu Hat base object holding service instances.</summary>
    public string ServiceInstanceBaseObject { get; set; } = "";
    /// <summary>Turuncu Hat select name carrying the affected service value.</summary>
    public string ServiceNameSelect { get; set; } = "";
    /// <summary>Turuncu Hat base object holding operational change records.</summary>
    public string ChangeBaseObject { get; set; } = "";
    /// <summary>Allowlisted maintenance profiles keyed by profile name.</summary>
    public Dictionary<string, MaintenanceProfileOptions> Profiles { get; set; } = [];
}

/// <summary>One protected maintenance profile. Recipients are a distribution-request audience only.</summary>
public sealed class MaintenanceProfileOptions
{
    /// <summary>Operator-visible label; falls back to the profile name when blank.</summary>
    public string Label { get; set; } = "";
    /// <summary>SCCM collection identifier; corporate value, configuration-owned only.</summary>
    public string CollectionId { get; set; } = "";
    /// <summary>Proposed system/application scope text.</summary>
    public string Scope { get; set; } = "";
    /// <summary>Proposed effect detail text.</summary>
    public string Impact { get; set; } = "";
    /// <summary>Proposed operator check text.</summary>
    public string Checks { get; set; } = "";
    /// <summary>Proposed description text; no date placeholder is interpolated from source values.</summary>
    public string Description { get; set; } = "";
    /// <summary>Base distribution-request To addresses.</summary>
    public string[] To { get; set; } = [];
    /// <summary>Profile-specific distribution-request Cc addresses.</summary>
    public string[] Cc { get; set; } = [];
    /// <summary>Legacy high-priority flag; recorded as review metadata, never a delivery action.</summary>
    public bool HighPriority { get; set; }
}

/// <summary>Server-owned Hangfire SQL job host configuration. The API only enqueues.</summary>
public sealed class HangfireOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Hangfire";

    /// <summary>Default-off job composition; disabled means submission fails closed.</summary>
    public bool Enabled { get; set; }
    /// <summary>Existing reviewed Hangfire schema name; the application never chooses a shared default silently.</summary>
    public string SchemaName { get; set; } = "HangFire";
    /// <summary>Must remain false. Schema provisioning is a separate DBA or isolated test-harness step.</summary>
    public bool PrepareSchema { get; set; }
    /// <summary>Queue this deployment's Worker consumes.</summary>
    public string Queue { get; set; } = "announcement-source";
    /// <summary>Worker thread count, bounded to 1-16; zero uses the source default of four.</summary>
    public int WorkerCount { get; set; }
    /// <summary>Storage queue poll interval.</summary>
    public int QueuePollIntervalSeconds { get; set; } = 5;
    /// <summary>Invisibility timeout covering a full source job plus overhead.</summary>
    public int InvisibilityTimeoutMinutes { get; set; } = 30;
}
