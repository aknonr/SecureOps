namespace SecureOps.Domain.OperationalRecords;

/// <summary>Durable state of the operational-record to Jira workflow. Numeric values are frozen by the v1 wire contract.</summary>
public enum OperationalRecordWorkflowState
{
    /// <summary>The record was imported from the source.</summary>
    Imported = 0,
    /// <summary>The record was classified by an approved rule.</summary>
    Classified = 1,
    /// <summary>The record requires explicit classification review.</summary>
    NeedsManualReview = 2,
    /// <summary>The record is eligible for Jira transfer.</summary>
    Eligible = 3,
    /// <summary>An operator generated a safe Jira preview.</summary>
    Previewed = 4,
    /// <summary>An authorized actor requested Jira creation.</summary>
    CreateRequested = 5,
    /// <summary>This workflow owns the current Jira-create attempt.</summary>
    CreatingJira = 6,
    /// <summary>The Jira issue key was persisted.</summary>
    JiraCreated = 7,
    /// <summary>The source-record close/update stage is running.</summary>
    ClosingOperationalRecord = 8,
    /// <summary>Jira creation and source-record close/update completed.</summary>
    Completed = 9,
    /// <summary>Jira creation failed before a trusted issue key was persisted.</summary>
    JiraCreateFailed = 10,
    /// <summary>Jira exists, but source-record close/update failed.</summary>
    OperationalRecordCloseFailed = 11
}
