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
    string? OperatorNotice = null);
