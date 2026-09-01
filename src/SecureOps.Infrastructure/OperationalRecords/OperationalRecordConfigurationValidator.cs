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
        TuruncuHatOptions turuncuHat = configuration.GetSection(TuruncuHatOptions.SectionName).Get<TuruncuHatOptions>() ?? new();

        if (!string.Equals(operational.SourceProvider, "Disabled", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(operational.SourceProvider, "Fake", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(operational.SourceProvider, "Simulation", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(operational.SourceProvider, "TuruncuHat", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"OperationalRecords:SourceProvider '{operational.SourceProvider}' is not implemented. Use Disabled, Simulation, Fake, or TuruncuHat.");
        }

        if (string.Equals(operational.SourceProvider, "Fake", StringComparison.OrdinalIgnoreCase)
            && !IsSyntheticEnvironment(environmentName))
        {
            throw new InvalidOperationException("OperationalRecords:SourceProvider Fake is permitted only in Development, Demo, or Test.");
        }

        if (string.Equals(operational.SourceProvider, "Simulation", StringComparison.OrdinalIgnoreCase)
            && !IsSyntheticEnvironment(environmentName))
        {
            throw new InvalidOperationException("OperationalRecords:SourceProvider Simulation is permitted only in Development, Demo, or Test.");
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

        bool readOnlyEnterpriseMode = operational.ReadOnlyIntegrationMode;
        if (string.Equals(operational.SourceProvider, "TuruncuHat", StringComparison.OrdinalIgnoreCase))
        {
            ValidateTuruncuHatReadConfiguration(turuncuHat);
            if (!readOnlyEnterpriseMode)
            {
                ValidateTuruncuHatWriteConfiguration(turuncuHat);
            }
        }

        if (!string.Equals(jira.Provider, "Disabled", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(jira.Provider, "Fake", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(jira.Provider, "Simulation", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(jira.Provider, "Corporate", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Jira:Provider must be Disabled, Simulation, Fake, or Corporate.");
        }

        if (string.Equals(jira.Provider, "Fake", StringComparison.OrdinalIgnoreCase)
            && !IsSyntheticEnvironment(environmentName))
        {
            throw new InvalidOperationException("Jira:Provider Fake is permitted only in Development, Demo, or Test.");
        }


        bool simulationSource = string.Equals(operational.SourceProvider, "Simulation", StringComparison.OrdinalIgnoreCase);
        bool simulationJira = string.Equals(jira.Provider, "Simulation", StringComparison.OrdinalIgnoreCase);
        if ((simulationSource || simulationJira) && (!simulationSource || !simulationJira))
        {
            throw new InvalidOperationException(
                "TEST simulation requires both OperationalRecords:SourceProvider and Jira:Provider to be Simulation.");
        }

        if (simulationJira && !IsSyntheticEnvironment(environmentName))
        {
            throw new InvalidOperationException("Jira:Provider Simulation is permitted only in Development, Demo, or Test.");
        }

        bool turuncuHatSource = string.Equals(operational.SourceProvider, "TuruncuHat", StringComparison.OrdinalIgnoreCase);
        bool corporateJira = string.Equals(jira.Provider, "Corporate", StringComparison.OrdinalIgnoreCase);
        if (operational.ReadOnlyIntegrationMode)
        {
            if (!string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("OperationalRecords:ReadOnlyIntegrationMode is permitted only in Test.");
            }

            if (!turuncuHatSource || !corporateJira)
            {
                throw new InvalidOperationException(
                    "OperationalRecords:ReadOnlyIntegrationMode requires OperationalRecords:SourceProvider=TuruncuHat and Jira:Provider=Corporate.");
            }
        }
        else if (string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase)
                 && (turuncuHatSource || corporateJira))
        {
            throw new InvalidOperationException(
                "Corporate providers in Test require OperationalRecords:ReadOnlyIntegrationMode=true so external writes fail closed.");
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

        ValidateJiraReporterPolicy(jira);
        if (!readOnlyEnterpriseMode)
        {
            ValidateJiraAssignmentPolicy(jira);
        }

        if (jira.SummaryMaxLength is < 32 or > 255)
        {
            throw new InvalidOperationException("Jira:SummaryMaxLength must be between 32 and 255.");
        }

        if (jira.ConnectTimeoutSeconds is < 1 or > 30
            || jira.RequestTimeoutSeconds is < 1 or > 120
            || jira.MaxResponseBytes is < 1024 or > 5_242_880
            || jira.UserSearchMaxAttempts is < 1 or > 3
            || jira.UserSearchRetryDelayMilliseconds is < 0 or > 5000)
        {
            throw new InvalidOperationException("Jira HTTP bounds are invalid.");
        }

        if (string.Equals(jira.Provider, "Corporate", StringComparison.OrdinalIgnoreCase))
        {
            ValidateCorporateJiraReadConfiguration(jira);
            if (!readOnlyEnterpriseMode)
            {
                ValidateCorporateJiraWriteConfiguration(jira);
            }
        }
    }

    private static void ValidateCorporateJiraReadConfiguration(JiraIntegrationOptions options)
    {
        ValidateHttpsBaseUrl(options.BaseUrl, "Jira:BaseUrl");
        RequireSecret(options.Authorization, "Jira:Authorization");
        if (!string.Equals(options.AuthenticationMode, "Basic", StringComparison.OrdinalIgnoreCase)
            || !IsValidBasicAuthorization(options.Authorization))
        {
            throw new InvalidOperationException("Corporate Jira requires the reviewed Jira:AuthenticationMode Basic and a runtime Basic Authorization value.");
        }

        if (string.IsNullOrWhiteSpace(options.ProjectKey)
            || string.IsNullOrWhiteSpace(options.IssueType)
            || string.IsNullOrWhiteSpace(options.MappingVersion)
            || string.IsNullOrEmpty(options.SummarySeparator)
            || options.SummarySeparator.Length > 10
            || !IsSafeIdentifier(options.ProjectKey))
        {
            throw new InvalidOperationException("Corporate Jira preview configuration is incomplete or invalid.");
        }
    }

    private static void ValidateCorporateJiraWriteConfiguration(JiraIntegrationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.IssueTypeId)
            || string.IsNullOrWhiteSpace(options.TeamCustomField)
            || string.IsNullOrWhiteSpace(options.TeamValue)
            || string.IsNullOrWhiteSpace(options.RequesterWatcherCustomField)
            || options.Labels.Length == 0
            || options.Labels.Any(string.IsNullOrWhiteSpace)
            || !long.TryParse(options.IssueTypeId, out long issueTypeId)
            || issueTypeId <= 0
            || options.TeamValue.Length > 256
            || !IsSafeIdentifier(options.TeamCustomField)
            || !IsSafeIdentifier(options.RequesterWatcherCustomField)
            || options.Labels.Any(label => label.Length > 128))
        {
            throw new InvalidOperationException("Corporate Jira mapping configuration is incomplete or invalid.");
        }
    }

    private static void ValidateJiraReporterPolicy(JiraIntegrationOptions options)
    {
        bool projectDefault = string.Equals(options.ReporterMode, "ProjectDefault", StringComparison.OrdinalIgnoreCase);
        bool authenticatedOperator = string.Equals(options.ReporterMode, "AuthenticatedOperator", StringComparison.OrdinalIgnoreCase);
        if (!projectDefault && !authenticatedOperator)
        {
            throw new InvalidOperationException("Jira:ReporterMode must be ProjectDefault or AuthenticatedOperator.");
        }
    }

    private static void ValidateJiraAssignmentPolicy(JiraIntegrationOptions options)
    {
        bool projectDefault = string.Equals(options.AssignmentMode, "ProjectDefault", StringComparison.OrdinalIgnoreCase);
        bool verifiedMapping = string.Equals(options.AssignmentMode, "VerifiedOperatorMapping", StringComparison.OrdinalIgnoreCase);
        if (!projectDefault && !verifiedMapping)
        {
            throw new InvalidOperationException("Jira:AssignmentMode must be ProjectDefault or VerifiedOperatorMapping.");
        }

        JiraOperatorAssigneeMappingOptions[] mappings = options.OperatorAssigneeMappings ?? [];
        if ((projectDefault && mappings.Length != 0)
            || (verifiedMapping && mappings.Length == 0)
            || mappings.Any(mapping => !IsSafeIdentity(mapping.SecureOpsActor) || !IsSafeIdentity(mapping.JiraUsername))
            || mappings.GroupBy(mapping => mapping.SecureOpsActor, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException(
                "Jira operator-assignee mappings must be empty for ProjectDefault, or non-empty, exact, bounded, and actor-unique for VerifiedOperatorMapping.");
        }
    }

    private static void ValidateTuruncuHatReadConfiguration(TuruncuHatOptions options)
    {
        ValidateHttpsBaseUrl(options.BaseUrl, "TuruncuHat:BaseUrl");
        RequireSecret(options.Authorization, "TuruncuHat:Authorization");
        RequireSecret(options.Username, "TuruncuHat:Username");
        RequireSecret(options.Password, "TuruncuHat:Password");
        if (options.TenantId <= 0
            || options.RelatedGroupId <= 0
            || options.ExcludedDccIds.Length == 0
            || options.ExcludedDccIds.Any(value => value <= 0)
            || !IsSafeIdentifier(options.SourceBaseObject))
        {
            throw new InvalidOperationException("TuruncuHat source-read configuration is incomplete or invalid.");
        }

        if (options.SessionIdSegmentIndex < 0
            || options.SessionLifetimeSeconds is < 1 or > 86_400
            || options.ConnectTimeoutSeconds is < 1 or > 30
            || options.RequestTimeoutSeconds is < 1 or > 120
            || options.MaxResponseBytes is < 1024 or > 5_242_880
            || options.MaxDescriptionLength is < 1 or > 8000)
        {
            throw new InvalidOperationException("TuruncuHat session or HTTP bounds are invalid.");
        }
    }

    private static void ValidateTuruncuHatWriteConfiguration(TuruncuHatOptions options)
    {
        if (options.ActivityTaskModelId <= 0
            || options.ActivityGroupId <= 0
            || options.ActivityMainObjectTypeId <= 0
            || options.CompletedStatusId <= 0
            || string.IsNullOrWhiteSpace(options.CompletionCommentTemplate)
            || !options.CompletionCommentTemplate.Contains("{JiraKey}", StringComparison.Ordinal)
            || options.CompletionCommentTemplate.Length > 1000
            || !IsSafeIdentifier(options.ActivityBaseObject))
        {
            throw new InvalidOperationException("TuruncuHat close mapping configuration is incomplete or invalid.");
        }
    }

    private static void ValidateHttpsBaseUrl(string value, string key)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException($"{key} must be an absolute HTTPS URL without embedded credentials.");
        }
    }

    private static void RequireSecret(string value, string key)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 4096
            || value.Contains('\r')
            || value.Contains('\n'))
        {
            throw new InvalidOperationException($"{key} is required and must be supplied through controlled runtime configuration.");
        }
    }

    private static bool IsSafeIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 128
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static bool IsSafeIdentity(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && value.Length <= 256
        && !value.Any(char.IsControl);

    private static bool IsValidBasicAuthorization(string value)
    {
        const string prefix = "Basic ";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> encoded = value.AsSpan(prefix.Length).Trim();
        Span<byte> decoded = stackalloc byte[3072];
        if (encoded.IsEmpty
            || !Convert.TryFromBase64Chars(encoded, decoded, out int bytesWritten))
        {
            return false;
        }

        int separator = decoded[..bytesWritten].IndexOf((byte)':');
        return separator > 0 && separator < bytesWritten - 1;
    }

    private static bool IsSyntheticEnvironment(string environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, "Demo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase);
}
