using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>
/// One account row while a plan's account list is being edited. Edits stay here until the server confirms the save, so a refused
/// request never loses what the person typed.
/// </summary>
public sealed class PlanAccountDraft
{
    /// <summary>Service account.</summary>
    public Guid AccountId { get; init; }

    /// <summary>Account name for display.</summary>
    public string AccountName { get; init; } = string.Empty;

    /// <summary>Domain, when recorded.</summary>
    public string? Domain { get; init; }

    /// <summary>Target gMSA name as typed; the server normalizes and validates it.</summary>
    public string TargetGmsaName { get; set; } = string.Empty;

    /// <summary>Open gMSA request the plan carries out; kept as the server returned it (the editor does not change it).</summary>
    public Guid? RequestId { get; init; }

    /// <summary>The request shape the API expects.</summary>
    public ChangePlanAccountInput ToInput() => new(AccountId, TargetGmsaName.Trim(), RequestId);

    /// <summary>Starts from the accounts the plan currently holds.</summary>
    public static List<PlanAccountDraft> From(IEnumerable<ChangePlanAccountView> accounts) =>
        [.. accounts.Select(a => new PlanAccountDraft
        {
            AccountId = a.AccountId,
            AccountName = a.AccountName,
            Domain = a.Domain,
            TargetGmsaName = a.TargetGmsaName,
            RequestId = a.RequestId
        })];
}
