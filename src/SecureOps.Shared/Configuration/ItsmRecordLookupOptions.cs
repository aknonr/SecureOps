namespace SecureOps.Shared.Configuration;

/// <summary>
/// Server-owned selection of the read-only ITSM record lookup (ADR-0031) and its tracking and user-match
/// extensions (ADR-0032). The provider is chosen only by configuration, defaults to Disabled, and never falls
/// back to Fake. Turuncu Hat credentials and HTTP bounds stay in <see cref="TuruncuHatOptions"/>; this section
/// adds no credential.
/// </summary>
public sealed class ItsmRecordLookupOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "ItsmRecordLookup";

    /// <summary>Lookup provider: Disabled, Fake (Development, Demo and Test only) or TuruncuHat.</summary>
    public string Provider { get; set; } = "Disabled";
    /// <summary>Turuncu Hat base object holding OR records; corporate value, configuration-owned only.</summary>
    public string OrBaseObject { get; set; } = string.Empty;
    /// <summary>Turuncu Hat base object holding OCO records; corporate value, configuration-owned only.</summary>
    public string OcoBaseObject { get; set; } = string.Empty;
    /// <summary>
    /// OR source field carrying the status code. Empty (default) means the field is not selected, because the
    /// field name is not evidenced yet (ADR-0032). Identifier characters only; configuration-owned.
    /// </summary>
    public string OrStatusField { get; set; } = string.Empty;
    /// <summary>OR source field carrying the stage code. Empty (default) means it is not selected (ADR-0032).</summary>
    public string OrStageField { get; set; } = string.Empty;
    /// <summary>
    /// Value of the comment <c>m_mob</c> column that identifies an OR record (ADR-0032). Empty means comment
    /// tracking is not configured and the comment part is Unknown/NotConfigured. Digits or identifier characters.
    /// </summary>
    public string CommentMainObject { get; set; } = string.Empty;
    /// <summary>
    /// Comment column holding the creation time; only its raw text of the newest comment is read (ADR-0032).
    /// The default is a source system column that is not yet proven for comments; if the source rejects it the
    /// comment part is Unknown. Identifier characters only.
    /// </summary>
    public string CommentTimeField { get; set; } = "m_created_dt";
    /// <summary>
    /// Optional value of <c>m_process.m_main_object</c> for OR records. Empty (default) sends the activity filter
    /// with the record id only, as the Turuncu Hat team described it. Digits or identifier characters.
    /// </summary>
    public string ActivityMainObject { get; set; } = string.Empty;
    /// <summary>
    /// OR source field carrying the assigned group code (TH-11). Empty (default) means it is not selected and the
    /// assignment class is Unknown. Identifier characters only; configuration-owned.
    /// </summary>
    public string OrAssignedGroupField { get; set; } = string.Empty;
    /// <summary>
    /// Source id of the WASAS group, digits only (TH-11). Empty (default) means no activity or assignment can be
    /// attributed to WASAS and both classes are Unknown. Corporate value; configuration-owned, never in the repository.
    /// </summary>
    public string WasasGroupId { get; set; } = string.Empty;
    /// <summary>Activity <c>m_status</c> code that means "approved" (owner-confirmed TEST observation; digits). Anything else on a completed row is NotApproved.</summary>
    public string ActivityApprovedStatus { get; set; } = "4";
    /// <summary>
    /// Allow-list of text fragments of the activity task-model display text that identify the WASAS review activity.
    /// Empty (default) means no activity can be identified and the activity class is Unknown (fail closed).
    /// </summary>
    public string[] ActivityTaskModelContains { get; set; } = [];
    /// <summary>Stage ids that mean the OR is closed in the source (digits). Empty means the stage is not used for closure.</summary>
    public string[] ClosedStageIds { get; set; } = [];
    /// <summary>Status ids that mean the OR is closed in the source (digits). Empty means the status is not used for closure.</summary>
    public string[] ClosedStatusIds { get; set; } = [];
}
