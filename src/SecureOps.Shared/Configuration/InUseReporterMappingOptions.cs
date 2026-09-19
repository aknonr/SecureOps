namespace SecureOps.Shared.Configuration;

/// <summary>Reviewed exact identity crosswalk; never a grant or a source-name routing rule.</summary>
public sealed class InUseReporterMappingOptions
{
    /// <summary>Server-owned configuration section, empty by default.</summary>
    public const string SectionName = "InUseReporterMapping";
    /// <summary>Source-owner review revision; changes invalidate displayed proposals.</summary>
    public string Revision { get; set; } = "";
    /// <summary>At most 1000 reviewed identity links; no inferred or seeded corporate values.</summary>
    public InUseReporterIdentityLink[] Links { get; set; } = [];
}

/// <summary>Exact persisted source namespace/reference to existing application identity.</summary>
public sealed record InUseReporterIdentityLink(string IdentityScope, string UserReference, Guid ApplicationUserId,
    string ReviewReference, DateTimeOffset ValidUntil);
