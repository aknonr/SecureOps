namespace SecureOps.Infrastructure.ServiceAccounts.Import;

/// <summary>Staged entity kinds.</summary>
public static class StagedKinds
{
    /// <summary>Account identity/attributes.</summary>
    public const string Account = "Account";
    /// <summary>Ownership proposal from source.</summary>
    public const string Ownership = "Ownership";
    /// <summary>Source observation.</summary>
    public const string Observation = "Observation";
    /// <summary>Absence observation generated for accounts not in this batch.</summary>
    public const string Absence = "Absence";
    /// <summary>Request.</summary>
    public const string Request = "Request";
    /// <summary>Action.</summary>
    public const string Action = "Action";
    /// <summary>Communication.</summary>
    public const string Communication = "Communication";
    /// <summary>Finding.</summary>
    public const string Finding = "Finding";
    /// <summary>Handover.</summary>
    public const string Handover = "Handover";
}

/// <summary>Canonical staged field names shared by all profiles.</summary>
public static class StagedFields
{
    /// <summary>Account name.</summary>
    public const string Account = "Account";
    /// <summary>Domain.</summary>
    public const string Domain = "Domain";
    /// <summary>Report organization.</summary>
    public const string Organization = "Organization";
    /// <summary>Group directorate observation.</summary>
    public const string GroupDirectorate = "GroupDirectorate";
    /// <summary>Owner team.</summary>
    public const string OwnerTeam = "OwnerTeam";
    /// <summary>Owner person.</summary>
    public const string OwnerPerson = "OwnerPerson";
    /// <summary>Consumer team.</summary>
    public const string ConsumerTeam = "ConsumerTeam";
    /// <summary>Source team (handover source).</summary>
    public const string SourceTeam = "SourceTeam";
    /// <summary>Target team.</summary>
    public const string TargetTeam = "TargetTeam";
    /// <summary>Password last set observation.</summary>
    public const string PasswordLastSet = "PasswordLastSet";
    /// <summary>AD or LDAP last logon observation.</summary>
    public const string LastLogonAdOrLdap = "LastLogonAdOrLdap";
    /// <summary>AD last logon observation.</summary>
    public const string LastLogonAd = "LastLogonAd";
    /// <summary>Comment.</summary>
    public const string Comment = "Comment";
    /// <summary>Handover flag.</summary>
    public const string HandoverFlag = "HandoverFlag";
    /// <summary>Notes.</summary>
    public const string Notes = "Notes";
    /// <summary>OR.</summary>
    public const string Or = "OR";
    /// <summary>OCO.</summary>
    public const string Oco = "OCO";
    /// <summary>Jira or other system record.</summary>
    public const string OtherRecord = "OtherRecord";
    /// <summary>Action type label.</summary>
    public const string ActionType = "ActionType";
    /// <summary>Request status label.</summary>
    public const string Status = "Status";
    /// <summary>Follow-up person.</summary>
    public const string FollowupPerson = "FollowupPerson";
    /// <summary>Contact person.</summary>
    public const string ContactPerson = "ContactPerson";
    /// <summary>Next follow-up date.</summary>
    public const string NextFollowup = "NextFollowup";
    /// <summary>First sent date.</summary>
    public const string FirstSent = "FirstSent";
    /// <summary>Last reply date.</summary>
    public const string LastReply = "LastReply";
    /// <summary>Plan start.</summary>
    public const string PlanStart = "PlanStart";
    /// <summary>Plan end.</summary>
    public const string PlanEnd = "PlanEnd";
    /// <summary>Plan announced date.</summary>
    public const string PlanAnnounced = "PlanAnnounced";
    /// <summary>Action date.</summary>
    public const string ActualDate = "ActualDate";
    /// <summary>Action result label.</summary>
    public const string Result = "Result";
    /// <summary>Performer team.</summary>
    public const string PerformerTeam = "PerformerTeam";
    /// <summary>Performer person.</summary>
    public const string PerformerPerson = "PerformerPerson";
    /// <summary>Record kind label.</summary>
    public const string RecordKind = "RecordKind";
    /// <summary>Evidence note.</summary>
    public const string Evidence = "Evidence";
    /// <summary>Verification date.</summary>
    public const string VerifiedDate = "VerifiedDate";
    /// <summary>Verifier person.</summary>
    public const string Verifier = "Verifier";
    /// <summary>Source note.</summary>
    public const string SourceNote = "SourceNote";
    /// <summary>Legacy display ID.</summary>
    public const string LegacyId = "LegacyId";
    /// <summary>Mail date.</summary>
    public const string OccurredOn = "OccurredOn";
    /// <summary>Direction.</summary>
    public const string Direction = "Direction";
    /// <summary>Communication kind.</summary>
    public const string CommunicationKind = "CommunicationKind";
    /// <summary>Contact team.</summary>
    public const string ContactTeam = "ContactTeam";
    /// <summary>Subject.</summary>
    public const string Subject = "Subject";
    /// <summary>Summary.</summary>
    public const string Summary = "Summary";
    /// <summary>Link.</summary>
    public const string Link = "Link";
    /// <summary>Meaningful reply flag label.</summary>
    public const string MeaningfulReply = "MeaningfulReply";
    /// <summary>Record scope label (account/team).</summary>
    public const string RecordScope = "RecordScope";
    /// <summary>Extra linked accounts (JSON array).</summary>
    public const string LinkedAccounts = "LinkedAccounts";
    /// <summary>Entered-by label.</summary>
    public const string EnteredBy = "EnteredBy";
    /// <summary>Handover status label.</summary>
    public const string HandoverStatus = "HandoverStatus";
    /// <summary>Handover proposed date.</summary>
    public const string ProposedOn = "ProposedOn";
    /// <summary>Handover accepted date (source claim only).</summary>
    public const string AcceptedOn = "AcceptedOn";
    /// <summary>Target action label.</summary>
    public const string TargetAction = "TargetAction";
    /// <summary>Plan / suitability note.</summary>
    public const string SuitabilityNote = "SuitabilityNote";
    /// <summary>Finding fields.</summary>
    public const string Server = "Server";
    /// <summary>Component type.</summary>
    public const string ComponentType = "ComponentType";
    /// <summary>Component name.</summary>
    public const string ComponentName = "ComponentName";
    /// <summary>Environment.</summary>
    public const string Environment = "Environment";
    /// <summary>Scan time.</summary>
    public const string ScanAt = "ScanAt";
    /// <summary>Scan result label.</summary>
    public const string ScanResult = "ScanResult";
    /// <summary>Match result label.</summary>
    public const string MatchResult = "MatchResult";
    /// <summary>Coverage window.</summary>
    public const string Coverage = "Coverage";
    /// <summary>Owning team.</summary>
    public const string OwningTeam = "OwningTeam";
    /// <summary>Job reference.</summary>
    public const string JobReference = "JobReference";
    /// <summary>Finding status label.</summary>
    public const string FindingStatus = "FindingStatus";
    /// <summary>Observation profile (coordination-list or dba-handover semantics).</summary>
    public const string ObservationProfile = "ObservationProfile";
}

/// <summary>One staged source row with its canonical fields and provenance.</summary>
/// <param name="Sheet">Sheet or section name.</param>
/// <param name="RowNumber">1-based source row number.</param>
/// <param name="Kind">Staged entity kind.</param>
/// <param name="Original">Original header → display value (kept verbatim for evidence).</param>
/// <param name="Fields">Canonical field → normalized value.</param>
/// <param name="Errors">Row errors (row cannot be committed).</param>
/// <param name="Warnings">Row warnings (visible, not blocking).</param>
/// <param name="LegacyReference">Stable legacy reconciliation key, if any.</param>
/// <param name="MigrationKey">Migration package key, if any.</param>
public sealed record StagedRow(string Sheet, int RowNumber, string Kind, IReadOnlyDictionary<string, string?> Original,
    IReadOnlyDictionary<string, string?> Fields, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings,
    string? LegacyReference, Guid? MigrationKey)
{
    /// <summary>Reads a canonical field.</summary>
    public string? this[string field] => Fields.TryGetValue(field, out string? value) ? value : null;
}

/// <summary>Parser output: rows plus the applied column mapping and file-level warnings.</summary>
/// <param name="Rows">Staged rows.</param>
/// <param name="Mapping">Applied column mapping.</param>
/// <param name="Warnings">File-level warnings.</param>
/// <param name="FormulaCells">Mapped cells whose value was a cached formula result.</param>
/// <param name="IgnoredHelperColumns">Helper/summary columns intentionally not imported.</param>
/// <param name="PersonAliases">Evidenced spelling pairs (alias, standard label) from a migration package.</param>
/// <param name="SuggestedReportDate">Source report date declared inside the file, shown for confirmation only.</param>
public sealed record StagedFile(IReadOnlyList<StagedRow> Rows, IReadOnlyList<SecureOps.Shared.Contracts.ServiceAccounts.ImportColumnMapping> Mapping,
    IReadOnlyList<string> Warnings, int FormulaCells, int IgnoredHelperColumns, IReadOnlyList<string[]> PersonAliases, DateOnly? SuggestedReportDate = null);
