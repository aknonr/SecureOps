using Microsoft.Extensions.Configuration;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Fail-fast validation for operational-record and Jira provider configuration.</summary>
public static class OperationalRecordConfigurationValidator
{
    /// <summary>Validates supported providers and bounded mapping values.</summary>
    public static void Validate(IConfiguration configuration, string environmentName)
    {
        OperationalRecordsOptions operational = configuration.GetSection(OperationalRecordsOptions.SectionName).Get<OperationalRecordsOptions>() ?? new();
        JiraIntegrationOptions jira = configuration.GetSection(JiraIntegrationOptions.SectionName).Get<JiraIntegrationOptions>() ?? new();

        if (!string.Equals(operational.SourceProvider, "Disabled", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(operational.SourceProvider, "Fake", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"OperationalRecords:SourceProvider '{operational.SourceProvider}' is not implemented. Use Disabled, or Fake only for Development/Demo/Test.");
        }

        if (string.Equals(operational.SourceProvider, "Fake", StringComparison.OrdinalIgnoreCase)
            && !IsSyntheticEnvironment(environmentName))
        {
            throw new InvalidOperationException("OperationalRecords:SourceProvider Fake is permitted only in Development, Demo, or Test.");
        }

        if (!string.Equals(operational.RepositoryProvider, "InMemory", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(operational.RepositoryProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("OperationalRecords:RepositoryProvider must be InMemory or SqlServer.");
        }

        if (operational.MaxImportCount is < 1 or > 500)
        {
            throw new InvalidOperationException("OperationalRecords:MaxImportCount must be between 1 and 500.");
        }

        if (operational.ClaimLeaseSeconds is < 30 or > 900)
        {
            throw new InvalidOperationException("OperationalRecords:ClaimLeaseSeconds must be between 30 and 900.");
        }

        if (string.Equals(operational.RepositoryProvider, "SqlServer", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(configuration.GetConnectionString(Audit.AuditConnectionStrings.SecureOpsDb)))
        {
            throw new InvalidOperationException("ConnectionStrings:SecureOpsDb is required when OperationalRecords:RepositoryProvider is SqlServer.");
        }

        if (!string.Equals(jira.Provider, "Disabled", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(jira.Provider, "Fake", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Jira:Provider must be Disabled, or Fake only for Development/Demo/Test.");
        }

        if (string.Equals(jira.Provider, "Fake", StringComparison.OrdinalIgnoreCase)
            && !IsSyntheticEnvironment(environmentName))
        {
            throw new InvalidOperationException("Jira:Provider Fake is permitted only in Development, Demo, or Test.");
        }

        if (string.IsNullOrWhiteSpace(jira.ProjectKey) || string.IsNullOrWhiteSpace(jira.IssueType) || string.IsNullOrWhiteSpace(jira.MappingVersion))
        {
            throw new InvalidOperationException("Jira project, issue type, and mapping version are required.");
        }

        if (!string.Equals(jira.UnresolvedRequesterPolicy, "Block", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(jira.UnresolvedRequesterPolicy, "ProceedUnassigned", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Jira:UnresolvedRequesterPolicy must be Block or ProceedUnassigned.");
        }

        if (jira.SummaryMaxLength is < 32 or > 255)
        {
            throw new InvalidOperationException("Jira:SummaryMaxLength must be between 32 and 255.");
        }
    }

    private static bool IsSyntheticEnvironment(string environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, "Demo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase);
}
