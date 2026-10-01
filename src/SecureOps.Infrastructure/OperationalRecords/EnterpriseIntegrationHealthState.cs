using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.OperationalRecords;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Tracks safe runtime availability without retaining dependency details.</summary>
public sealed class EnterpriseIntegrationHealthState
{
    private readonly ConcurrentDictionary<string, bool> _unavailable = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Marks a provider operation successful.</summary>
    public void MarkAvailable(string provider) => _unavailable[provider] = false;

    /// <summary>Marks a provider unavailable.</summary>
    public void MarkUnavailable(string provider) => _unavailable[provider] = true;

    /// <summary>Returns Configured or Unavailable.</summary>
    public string GetConfiguredStatus(string provider) =>
        _unavailable.TryGetValue(provider, out bool unavailable) && unavailable
            ? "Unavailable"
            : "Configured";
}

/// <summary>Builds the safe administrator diagnostics contract.</summary>
public sealed class EnterpriseIntegrationDiagnostics
{
    private readonly OperationalRecordsOptions _operational;
    private readonly JiraIntegrationOptions _jira;
    private readonly EnterpriseIntegrationHealthState _health;

    /// <summary>Initializes diagnostics.</summary>
    public EnterpriseIntegrationDiagnostics(
        IOptions<OperationalRecordsOptions> operational,
        IOptions<JiraIntegrationOptions> jira,
        EnterpriseIntegrationHealthState health)
    {
        _operational = operational.Value;
        _jira = jira.Value;
        _health = health;
    }

    /// <summary>Returns safe provider state.</summary>
    public EnterpriseIntegrationHealthResponse Get()
    {
        bool simulation = string.Equals(_operational.SourceProvider, "Simulation", StringComparison.OrdinalIgnoreCase)
            && string.Equals(_jira.Provider, "Simulation", StringComparison.OrdinalIgnoreCase);
        return new(
            Provider("TuruncuHat", _operational.SourceProvider),
            Provider("Jira", _jira.Provider),
            simulation,
            simulation ? SimulationOperationalRecordClient.OperatorNotice : null,
            _operational.ReadOnlyIntegrationMode,
            _operational.ReadOnlyIntegrationMode ? ExternalIntegrationNotices.RealDataReadOnly : null);
    }

    private IntegrationProviderHealthResponse Provider(string provider, string selection) =>
        new(
            provider,
            selection,
            string.Equals(selection, "Disabled", StringComparison.OrdinalIgnoreCase)
                ? "Disabled"
                : _health.GetConfiguredStatus(provider));
}
