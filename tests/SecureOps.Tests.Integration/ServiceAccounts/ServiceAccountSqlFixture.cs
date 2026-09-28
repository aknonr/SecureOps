using System.Security.Claims;
using System.Collections.Concurrent;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SecureOps.Domain.Access;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>Runs only against an explicitly supplied disposable database created by tests/sql/service-accounts.</summary>
public sealed class ServiceAccountSqlFactAttribute : FactAttribute
{
    /// <summary>Environment variable holding the isolated test connection.</summary>
    public const string Variable = "SECUREOPS_SA_SQL_TEST_CONNECTION";

    public ServiceAccountSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = "NOT RUN: isolated Service Accounts SQL database (svcacct candidate) is unavailable.";
        }
    }
}

/// <summary>Synthetic principal with persisted capabilities.</summary>
internal sealed record SynUser(ApplicationUser User, ClaimsPrincipal Principal);

/// <summary>Synthetic users, scope grants and a service wired to the real SQL repository.</summary>
internal sealed class ServiceAccountSqlFixture
{
    private readonly Dictionary<string, ApplicationUser> _users = new(StringComparer.Ordinal);
    private readonly Guid _grantor = Guid.NewGuid();
    public ConcurrentQueue<string> SqlDiagnostics { get; } = new();

    public ServiceAccountSqlFixture()
    {
        Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SecureOpsDb"] = Environment.GetEnvironmentVariable(ServiceAccountSqlFactAttribute.Variable),
            ["ServiceAccounts:Provider"] = "SqlServer"
        }).Build();
        Repository = new SqlServiceAccountRepository(Configuration);
        IApplicationAccessService access = Substitute.For<IApplicationAccessService>();
        access.GetCurrentAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<AccessOperationContext>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                string name = call.Arg<ClaimsPrincipal>().Identity?.Name ?? string.Empty;
                return _users.TryGetValue(name, out ApplicationUser? user)
                    ? AccessServiceResult<EnsureAccessUserResult>.Success(new EnsureAccessUserResult(user, null, false, false))
                    : AccessServiceResult<EnsureAccessUserResult>.Fail("AccessDenied");
            });
        Service = new ServiceAccountService(Repository, access, Options.Create(new ServiceAccountOptions { Provider = "SqlServer" }),
            TimeProvider.System, new SafeSqlLogger(SqlDiagnostics));
        Suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    }

    public IConfiguration Configuration { get; }

    public SqlServiceAccountRepository Repository { get; }

    public ServiceAccountService Service { get; }

    /// <summary>Unique suffix isolating this test's synthetic names inside the shared disposable database.</summary>
    public string Suffix { get; }

    public AccessOperationContext Context { get; } = new("synthetic", "sa-test-" + Guid.NewGuid().ToString("N")[..12], null);

    public SqlConnection Connection() => new(Configuration.GetConnectionString("SecureOpsDb"));

    public async Task<SynUser> UserAsync(params string[] capabilities)
    {
        var id = Guid.NewGuid();
        string identity = "synthetic:sa:" + id.ToString("N");
        await using SqlConnection connection = Connection();
        await connection.ExecuteAsync("""
            INSERT INTO security.Users(UserId, CorporateIdentity, AuthenticationSource, AccessStatus, DisplayName)
            VALUES(@id, @identity, 'oidc', 'Approved', @display);
            """, new { id, identity, display = "Sentetik " + id.ToString("N")[..6] });
        ApplicationUser user = new(id, identity, "oidc", AccessStatus.Approved, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, 1, ["Synthetic"], capabilities);
        _users[identity] = user;
        return new SynUser(user, new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, identity)], "Synthetic")));
    }

    public async Task<Guid> OrganizationAsync(string name)
    {
        var id = Guid.NewGuid();
        await using SqlConnection connection = Connection();
        await connection.ExecuteAsync("""
            INSERT INTO svcacct.Organizations(Id, Name, NormalizedName, Kind, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
            VALUES(@id, @name, @key, 'Department', SYSUTCDATETIME(), @grantor, SYSUTCDATETIME(), @grantor);
            """, new { id, name, key = ServiceAccountText.LabelKey(name), grantor = _grantor });
        return id;
    }

    public async Task<Guid> TeamAsync(string name, Guid? organization)
    {
        var id = Guid.NewGuid();
        await using SqlConnection connection = Connection();
        await connection.ExecuteAsync("""
            INSERT INTO svcacct.Teams(Id, OrganizationId, Name, NormalizedName, Provisional, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
            VALUES(@id, @organization, @name, @key, 0, SYSUTCDATETIME(), @grantor, SYSUTCDATETIME(), @grantor);
            """, new { id, organization, name, key = ServiceAccountText.LabelKey(name), grantor = _grantor });
        return id;
    }

    public async Task GrantAsync(SynUser user, ScopeKind kind, Guid? organization = null, Guid? team = null)
    {
        await using SqlConnection connection = Connection();
        await connection.ExecuteAsync("""
            INSERT INTO svcacct.ScopeGrants(Id, UserId, ScopeKind, OrganizationId, TeamId, Reason, GrantedBy, GrantedAt)
            VALUES(NEWID(), @UserId, @kind, @organization, @team, 'Synthetic test scope', @grantor, SYSUTCDATETIME());
            """, new { UserId = user.User.Id, kind = kind.ToString(), organization, team, grantor = _grantor });
    }

    public async Task<int> CountAsync(string sql, object? parameters = null)
    {
        await using SqlConnection connection = Connection();
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    private sealed class SafeSqlLogger(ConcurrentQueue<string> entries) : ILogger<ServiceAccountService>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => EmptyScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (state is not IEnumerable<KeyValuePair<string, object?>> values)
            { return; }
            var fields = values.ToDictionary(x => x.Key, x => x.Value);
            if (!fields.ContainsKey("SqlNumber"))
            { return; }
            entries.Enqueue($"Number={fields["SqlNumber"]} State={fields["SqlState"]} Class={fields["SqlClass"]} CorrelationId={fields["CorrelationId"]}");
        }

        private sealed class EmptyScope : IDisposable
        {
            public static readonly EmptyScope Instance = new();
            public void Dispose() { }
        }
    }
}
