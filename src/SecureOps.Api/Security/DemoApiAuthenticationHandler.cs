using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SecureOps.Api.Security;

/// <summary>
/// Authentication handler for the non-production demo API bridge.
/// </summary>
public sealed class DemoApiAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly DemoApiAuthOptions _demoOptions;
    private readonly IHostEnvironment _environment;

    /// <summary>
    /// Initializes a new demo authentication handler.
    /// </summary>
    /// <param name="options">Scheme options monitor.</param>
    /// <param name="logger">Logger factory.</param>
    /// <param name="encoder">URL encoder.</param>
    /// <param name="demoOptions">Demo bridge options.</param>
    /// <param name="environment">Host environment.</param>
    public DemoApiAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<DemoApiAuthOptions> demoOptions,
        IHostEnvironment environment)
        : base(options, logger, encoder)
    {
        _demoOptions = demoOptions.Value;
        _environment = environment;
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!DemoApiAuthentication.IsAllowedEnvironment(_environment.EnvironmentName))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Request.Headers.TryGetValue(_demoOptions.HeaderName, out Microsoft.Extensions.Primitives.StringValues values))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string actorKey = values.ToString().Trim();
        if (!string.Equals(actorKey, DemoApiAuthentication.PlatformAdminActor, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(actorKey, DemoApiAuthentication.TeamLeadActor, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        Claim[] claims =
        [
            new(ClaimTypes.Name, $"demo:{actorKey}"),
            new("secureops:auth_source", "demo-api-bridge")
        ];

        ClaimsIdentity identity = new(claims, DemoApiAuthentication.SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        AuthenticationTicket ticket = new(new ClaimsPrincipal(identity), DemoApiAuthentication.SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
