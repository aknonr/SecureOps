using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Sessions;

namespace SecureOps.Tests.Unit.Sessions;

public sealed class SqlApplicationSessionRepositoryTests
{
    [Theory]
    [InlineData("SqlServer", true)]
    [InlineData("sqlserver", true)]
    [InlineData("File", false)]
    [InlineData("InMemory", false)]
    [InlineData(null, false)]
    public void TerminalTransition_ProviderSelection_AdvertisesAtomicAuditOnlyForSqlServer(string? provider, bool atomic)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SecureOpsDb"] = "Server=synthetic.invalid;Database=Synthetic;Integrated Security=true",
            ["Audit:Provider"] = provider
        }).Build();

        SqlApplicationSessionRepository repository = new(configuration);

        repository.WritesAuditAtomically.Should().Be(atomic);
    }
}
