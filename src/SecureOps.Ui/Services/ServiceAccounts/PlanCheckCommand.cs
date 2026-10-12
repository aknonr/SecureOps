using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>One checklist mark to send: the preview row and the request (state, note and the latest mark the person saw).</summary>
/// <param name="ItemId">Preview row.</param>
/// <param name="Request">What to record.</param>
public sealed record PlanCheckCommand(Guid ItemId, CheckChangePlanItemRequest Request);
