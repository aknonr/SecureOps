namespace SecureOps.Shared.Contracts.OperationalRecords;

/// <summary>Safe status for one configured enterprise integration provider.</summary>
public sealed record IntegrationProviderHealthResponse(
    string Provider,
    string Selection,
    string Status);

/// <summary>Safe Turuncu Hat and Jira provider diagnostics.</summary>
public sealed record EnterpriseIntegrationHealthResponse(
    IntegrationProviderHealthResponse TuruncuHat,
    IntegrationProviderHealthResponse Jira,
    bool SimulationMode = false,
    string? OperatorNotice = null,
    bool ReadOnlyIntegrationMode = false,
    string? ReadOnlyNotice = null);

/// <summary>Stable external-integration mode notices supplied by the backend.</summary>
public static class ExternalIntegrationNotices
{
    /// <summary>Operator notice for real provider reads with every external write fenced.</summary>
    public const string RealDataReadOnly = "GERÇEK VERİ — YAZMA KAPALI";
}
