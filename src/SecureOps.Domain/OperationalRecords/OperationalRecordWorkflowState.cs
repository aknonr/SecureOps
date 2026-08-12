namespace SecureOps.Domain.OperationalRecords;

/// <summary>Durable state of the operational-record to Jira workflow.</summary>
public enum OperationalRecordWorkflowState
{
    /// <summary>The record was imported from the source.</summary>
    Imported,
    /// <summary>The record was classified by an approved rule.</summary>
    Classified,
    /// <summary>The record requires explicit classification review.</summary>
    NeedsManualReview,
    /// <summary>The record is eligible for Jira transfer.</summary>
    Eligible,
    /// <summary>An operator generated a safe Jira preview.</summary>
    Previewed,
    /// <summary>An authorized actor requested Jira creation.</summary>
    CreateRequested,
    /// <summary>This workflow owns the current Jira-create attempt.</summary>
    CreatingJira,
    /// <summary>The Jira issue key was persisted.</summary>
    JiraCreated,
    /// <summary>The source-record close/update stage is running.</summary>
    ClosingOperationalRecord,
    /// <summary>Jira creation and source-record close/update completed.</summary>
    Completed,
    /// <summary>Jira creation failed before a trusted issue key was persisted.</summary>
    JiraCreateFailed,
    /// <summary>Jira exists, but source-record close/update failed.</summary>
    OperationalRecordCloseFailed
}
