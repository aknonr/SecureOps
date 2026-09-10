using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

/// <summary>Offline transition contract for a future verified executor. No retry from failed or uncertain writes.</summary>
public static class InUseCompletionTransitions
{
    /// <summary>Records one authoritative result; request scope/authorization remain executor responsibilities.</summary>
    public static InUseCompletion Apply(InUseCompletion state, string outcome, string? remoteId = null) => (state.Stage, outcome) switch
    {
        ("UploadPending", "Unknown") or ("CompletionPending", "Unknown") or ("VerificationPending", "Unknown") => state with { Stage = "ReconciliationRequired" },
        ("UploadPending", "Failed") => state with { Stage = "UploadFailed" },
        ("UploadPending", "Succeeded") when !string.IsNullOrWhiteSpace(remoteId) => state with { Stage = "TaskLookupPending", AttachmentId = remoteId },
        ("TaskLookupPending", "UniqueAuthorized") when !string.IsNullOrWhiteSpace(remoteId) => state with { Stage = "CompletionPending", TaskId = remoteId },
        ("TaskLookupPending", "MissingOrAmbiguous") => state with { Stage = "TaskLookupBlocked" },
        ("CompletionPending", "Failed") => state with { Stage = "CompletionFailed" },
        ("CompletionPending", "Succeeded") => state with { Stage = "VerificationPending" },
        ("VerificationPending", "Closed") => state with { Stage = "ClosedVerified", FinalOrState = "Closed" },
        ("VerificationPending", "StillOpen") when !string.IsNullOrWhiteSpace(remoteId) => state with { Stage = "TaskCompletedOrOpen", FinalOrState = remoteId },
        _ => throw new InvalidOperationException("Unsupported transition; blocked/failed/uncertain writes require reviewed reconciliation.")
    };
}
