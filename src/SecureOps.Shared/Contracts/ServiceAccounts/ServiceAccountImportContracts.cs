namespace SecureOps.Shared.Contracts.ServiceAccounts;

/// <summary>Import profile codes.</summary>
public static class ServiceAccountImportProfiles
{
    /// <summary>Periodic coordination list (evidenced seven-column header).</summary>
    public const string CoordinationList = "coordination-list";
    /// <summary>DBA handover list (evidenced four-column header).</summary>
    public const string DbaHandover = "dba-handover";
    /// <summary>Legacy JSON migration package.</summary>
    public const string LegacyPackage = "legacy-package";
    /// <summary>Legacy tracking workbook input sheets.</summary>
    public const string LegacyWorkbook = "legacy-workbook";
    /// <summary>Bounded generic mapping for files with not-yet-seen headers.</summary>
    public const string Generic = "generic";

    /// <summary>All supported profiles.</summary>
    public static IReadOnlyList<string> All { get; } = [CoordinationList, DbaHandover, LegacyPackage, LegacyWorkbook, Generic];
}

/// <summary>Row classification codes shown in preview.</summary>
public static class ServiceAccountImportClasses
{
    /// <summary>New record.</summary>
    public const string New = "New";
    /// <summary>Existing account, new source observation.</summary>
    public const string ObservationUpdate = "ObservationUpdate";
    /// <summary>Existing record gets additional links/fields only where empty.</summary>
    public const string Update = "Update";
    /// <summary>Nothing new.</summary>
    public const string Same = "Same";
    /// <summary>Conflicts with existing data; needs a decision.</summary>
    public const string Conflict = "Conflict";
    /// <summary>Invalid row; skipped.</summary>
    public const string Invalid = "Invalid";
    /// <summary>Existing account not present in this source batch (observation only).</summary>
    public const string NotSeen = "NotSeen";
    /// <summary>Outside the importer's scope; cannot be committed.</summary>
    public const string OutOfScope = "OutOfScope";
}

/// <summary>
/// Declared completeness of a source list. Only a complete list over a declared, validated population may produce
/// "not seen in this list" observations; a partial or unknown list never implies that an account is absent.
/// </summary>
public static class ServiceAccountImportCoverage
{
    /// <summary>Completeness not declared (default): no absence is inferred.</summary>
    public const string Unknown = "Unknown";
    /// <summary>Known to be a subset: no absence is inferred.</summary>
    public const string Partial = "Partial";
    /// <summary>Complete list for the declared organizations (and domain, when declared).</summary>
    public const string Complete = "Complete";

    /// <summary>All coverage codes.</summary>
    public static IReadOnlyList<string> All { get; } = [Unknown, Partial, Complete];
}

/// <summary>Bounded generic mapping target fields.</summary>
public static class ServiceAccountImportFields
{
    /// <summary>Account name (required).</summary>
    public const string AccountName = "AccountName";
    /// <summary>Domain.</summary>
    public const string Domain = "Domain";
    /// <summary>Report organization label.</summary>
    public const string Organization = "Organization";
    /// <summary>Owner team label (proposal only).</summary>
    public const string OwnerTeam = "OwnerTeam";
    /// <summary>Consuming team label.</summary>
    public const string ConsumerTeam = "ConsumerTeam";
    /// <summary>Password last set observation.</summary>
    public const string PasswordLastSet = "PasswordLastSet";
    /// <summary>Last logon observation.</summary>
    public const string LastLogon = "LastLogon";
    /// <summary>Comment observation.</summary>
    public const string Comment = "Comment";

    /// <summary>All generic targets.</summary>
    public static IReadOnlyList<string> All { get; } = [AccountName, Domain, Organization, OwnerTeam, ConsumerTeam, PasswordLastSet, LastLogon, Comment];
}

/// <summary>Column mapping line: source header to module field.</summary>
public sealed record ImportColumnMapping(string SourceHeader, string? TargetField, bool Formula = false, bool Ignored = false, string? Note = null);

/// <summary>Stage parameters (sent with the multipart file).</summary>
public sealed record StageImportRequest(
    string Profile,
    DateOnly? SourceReportDate,
    string SourceDateProvenance,
    string? DeclaredScope = null,
    string? DeclaredDomain = null,
    string? Sheet = null,
    string? TargetTeam = null,
    IReadOnlyList<ImportColumnMapping>? Mapping = null,
    string? Coverage = null,
    IReadOnlyList<Guid>? CoverageOrganizationIds = null);

/// <summary>Preview counts by classification and entity.</summary>
public sealed record ImportSummary(
    int TotalRows,
    int New,
    int ObservationUpdates,
    int Updates,
    int Same,
    int Conflicts,
    int Invalid,
    int NotSeen,
    int OutOfScope,
    int DecisionsRequired,
    int DecisionsMade,
    IReadOnlyDictionary<string, int> ByEntity,
    IReadOnlyList<string> NewTeamLabels,
    IReadOnlyList<string> NewOrganizationLabels,
    int IgnoredHelperColumns,
    int FormulaCells,
    int CohortFlagged,
    string Coverage = ServiceAccountImportCoverage.Unknown,
    int CoveragePopulation = 0,
    int CoverageOutsideRows = 0);

/// <summary>Import batch state.</summary>
public sealed record ImportBatchView(
    Guid Id,
    string Profile,
    string FileName,
    string Sha256,
    DateOnly? SourceReportDate,
    string SourceDateProvenance,
    string? DeclaredScope,
    string? DeclaredDomain,
    string Status,
    int PreviewVersion,
    int DecisionVersion,
    ImportSummary? Summary,
    IReadOnlyList<ImportColumnMapping> Mapping,
    IReadOnlyList<string> Warnings,
    bool Replay,
    DateTimeOffset UploadedAt,
    DateTimeOffset? CommittedAt,
    ImportResultView? Result,
    string Coverage = ServiceAccountImportCoverage.Unknown,
    IReadOnlyList<Guid>? CoverageOrganizationIds = null);

/// <summary>Old/new value pair.</summary>
public sealed record ImportFieldDiff(string Field, string? Current, string? Proposed, string Effect);

/// <summary>Match candidate for an ambiguous row.</summary>
public sealed record ImportCandidate(Guid Id, string Label, string Reason);

/// <summary>One staged row in preview.</summary>
public sealed record ImportRowView(
    int RowKey,
    string Sheet,
    int RowNumber,
    string EntityKind,
    string Classification,
    string? AccountLabel,
    IReadOnlyList<ImportFieldDiff> Diff,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ImportCandidate> Candidates,
    bool RequiresDecision,
    string? Decision,
    IReadOnlyList<string> AllowedDecisions);

/// <summary>Paged preview rows.</summary>
public sealed record ImportRowPage(IReadOnlyList<ImportRowView> Items, int Total, int Page, int PageSize);

/// <summary>One row decision.</summary>
public sealed record ImportRowDecision(int RowKey, string Decision, Guid? TargetId = null, string? Note = null);

/// <summary>Versioned decision update; stale versions return 409.</summary>
public sealed record ImportDecisionsRequest(int ExpectedDecisionVersion, IReadOnlyList<ImportRowDecision> Decisions,
    string? BulkClassification = null, string? BulkEntityKind = null, string? BulkDecision = null);

/// <summary>Commit revalidates preview and decision versions.</summary>
public sealed record ImportCommitRequest(int PreviewVersion, int DecisionVersion);

/// <summary>Persisted commit result; a replay returns the same result.</summary>
public sealed record ImportResultView(
    int AccountsCreated,
    int ObservationsRecorded,
    int NotSeenObservations,
    int OwnershipProposed,
    int OwnershipConfirmed,
    int RequestsCreated,
    int ActionsCreated,
    int CommunicationsCreated,
    int CommunicationLinksAdded,
    int FindingsCreated,
    int HandoversCreated,
    int TransitionsCreated,
    int TeamsCreated,
    int OrganizationsCreated,
    int PeopleCreated,
    int Unchanged,
    int Skipped,
    int Invalid,
    DateTimeOffset CommittedAt);

/// <summary>Import history list row.</summary>
public sealed record ImportHistoryItem(Guid Id, string Profile, string FileName, DateOnly? SourceReportDate, string Status,
    DateTimeOffset UploadedAt, DateTimeOffset? CommittedAt, int TotalRows);
