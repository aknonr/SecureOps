using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SecureOps.Infrastructure.Persistence;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Tests.Unit.Persistence;

public sealed class SqlPersistenceConfigurationValidatorTests
{
    [Fact]
    public void Validate_WhenSqlProviderUsesBoundedIntegratedConnection_Succeeds()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["Access:RepositoryProvider"] = "SqlServer",
            ["SessionSecurity:RepositoryProvider"] = "SqlServer",
            ["ConnectionStrings:SecureOpsDb"] = "Server=sql.invalid;Database=SecureOps;Integrated Security=True;Connect Timeout=15"
        });

        Action act = () => SqlPersistenceConfigurationValidator.Validate(configuration);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void Validate_WhenSqlConnectTimeoutIsUnboundedOrExcessive_FailsStartup(int timeout)
    {
        IConfiguration configuration = SqlConfiguration(
            $"Server=sql.invalid;Database=SecureOps;Integrated Security=True;Connect Timeout={timeout}");

        Action act = () => SqlPersistenceConfigurationValidator.Validate(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Connect Timeout*between 1 and 60*");
    }

    [Theory]
    [InlineData("Server=sql.invalid;Integrated Security=True")]
    [InlineData("Database=SecureOps;Integrated Security=True")]
    [InlineData("not-a-connection-string")]
    public void Validate_WhenSqlConnectionContractIsIncomplete_FailsWithoutEchoingConfiguration(string connectionString)
    {
        IConfiguration configuration = SqlConfiguration(connectionString);

        Action act = () => SqlPersistenceConfigurationValidator.Validate(configuration);

        InvalidOperationException exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("ConnectionStrings:SecureOpsDb").And.NotContain(connectionString);
    }

    [Fact]
    public void Validate_WhenIntegratedSecurityIsDisabled_FailsStartup()
    {
        IConfiguration configuration = SqlConfiguration(
            "Server=sql.invalid;Database=SecureOps;Integrated Security=False;Connect Timeout=15");

        Action act = () => SqlPersistenceConfigurationValidator.Validate(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Integrated Security*without SQL login credentials*");
    }

    [Fact]
    public async Task HealthReporter_WhenSqlIsNotConfigured_DoesNotProbe()
    {
        RecordingProbe probe = new();
        SqlPersistenceHealthReporter reporter = Reporter(Configuration(new Dictionary<string, string?>()), probe);

        SqlPersistenceHealthResponse response = await reporter.GetHealthAsync(CancellationToken.None);

        response.Should().Be(new SqlPersistenceHealthResponse("NotConfigured", false, null));
        probe.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task HealthReporter_WhenProbeSucceeds_ReportsHealthy()
    {
        RecordingProbe probe = new();
        SqlPersistenceHealthReporter reporter = Reporter(SqlConfiguration(), probe);

        SqlPersistenceHealthResponse response = await reporter.GetHealthAsync(CancellationToken.None);

        response.Should().Be(new SqlPersistenceHealthResponse("Healthy", true, null));
        probe.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task HealthReporter_WhenProbeFails_ReturnsOnlyStableSanitizedFailure()
    {
        RecordingProbe probe = new(new InvalidOperationException("sensitive provider detail"));
        SqlPersistenceHealthReporter reporter = Reporter(SqlConfiguration(), probe);

        SqlPersistenceHealthResponse response = await reporter.GetHealthAsync(CancellationToken.None);

        response.Should().Be(new SqlPersistenceHealthResponse("Unhealthy", true, OperationalErrorCodes.PersistenceUnavailable));
        response.ToString().Should().NotContain("sensitive provider detail");
    }

    private static SqlPersistenceHealthReporter Reporter(IConfiguration configuration, ISqlPersistenceProbe probe) =>
        new(configuration, probe, NullLogger<SqlPersistenceHealthReporter>.Instance);

    private static IConfiguration SqlConfiguration(string connectionString = "Server=sql.invalid;Database=SecureOps;Integrated Security=True;Connect Timeout=15") =>
        Configuration(new Dictionary<string, string?>
        {
            ["Audit:Provider"] = "SqlServer",
            ["ConnectionStrings:SecureOpsDb"] = connectionString
        });

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class RecordingProbe(Exception? exception = null) : ISqlPersistenceProbe
    {
        public int CallCount { get; private set; }

        public Task ProbeAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return exception is null ? Task.CompletedTask : Task.FromException(exception);
        }
    }
}
