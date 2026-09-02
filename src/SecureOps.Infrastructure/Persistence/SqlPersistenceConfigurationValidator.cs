using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Persistence;

/// <summary>Validates the shared SQL Server persistence connection contract.</summary>
public static class SqlPersistenceConfigurationValidator
{
    private const int MaximumConnectTimeoutSeconds = 60;

    /// <summary>Validates a bounded SQL connection when any durable SQL provider is selected.</summary>
    public static void Validate(IConfiguration configuration)
    {
        if (!IsSqlServerConfigured(configuration))
        {
            return;
        }

        string? connectionString = configuration.GetConnectionString(AuditConnectionStrings.SecureOpsDb);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:SecureOpsDb is required when a SqlServer persistence provider is configured.");
        }

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("ConnectionStrings:SecureOpsDb must be a valid SQL Server connection string.", exception);
        }

        if (string.IsNullOrWhiteSpace(builder.DataSource) || string.IsNullOrWhiteSpace(builder.InitialCatalog))
        {
            throw new InvalidOperationException("ConnectionStrings:SecureOpsDb must identify a SQL Server and database.");
        }

        if (!builder.IntegratedSecurity
            || !string.IsNullOrEmpty(builder.UserID)
            || !string.IsNullOrEmpty(builder.Password))
        {
            throw new InvalidOperationException("ConnectionStrings:SecureOpsDb must use Integrated Security without SQL login credentials.");
        }

        if (builder.ConnectTimeout is < 1 or > MaximumConnectTimeoutSeconds)
        {
            throw new InvalidOperationException($"ConnectionStrings:SecureOpsDb Connect Timeout must be between 1 and {MaximumConnectTimeoutSeconds} seconds.");
        }
    }

    /// <summary>Returns whether at least one runtime persistence provider selects SQL Server.</summary>
    public static bool IsSqlServerConfigured(IConfiguration configuration) =>
        IsSqlServer(configuration[$"{AuditOptions.SectionName}:Provider"])
        || IsSqlServer(configuration[$"{AccessOptions.SectionName}:RepositoryProvider"])
        || IsSqlServer(configuration[$"{SessionSecurityOptions.SectionName}:RepositoryProvider"])
        || IsSqlServer(configuration[$"{OperationalRecordsOptions.SectionName}:RepositoryProvider"]);

    private static bool IsSqlServer(string? value) =>
        string.Equals(value, "SqlServer", StringComparison.OrdinalIgnoreCase);
}
