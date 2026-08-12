using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SecureOps.Ui.Configuration;

namespace SecureOps.Ui.Services;

/// <summary>
/// Adds the demo API actor header to outbound identity lookup API calls.
/// </summary>
/// <remarks>
/// The demo shell never forwards its own authentication cookie to the API. Instead, in a
/// Development or Demo environment it attaches a fixed demo actor key that the API's
/// non-production demo authentication bridge maps to a role. The handler adds nothing when
/// demo mode is disabled or when the environment is not Development/Demo.
/// </remarks>
public sealed class DemoApiAuthHeaderHandler : DelegatingHandler
{
    private readonly IHostEnvironment _environment;
    private readonly DemoModeOptions _options;

    /// <summary>
    /// Initializes a new demo API auth header handler.
    /// </summary>
    /// <param name="environment">Host environment.</param>
    /// <param name="options">Demo mode options.</param>
    public DemoApiAuthHeaderHandler(IHostEnvironment environment, IOptions<DemoModeOptions> options)
    {
        _environment = environment;
        _options = options.Value;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (IsDemoActorEnabled())
        {
            request.Headers.Remove(_options.ApiDemoActorHeader);
            request.Headers.Add(_options.ApiDemoActorHeader, _options.ApiDemoActor);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private bool IsDemoActorEnabled()
    {
        bool allowedEnvironment = _environment.IsDevelopment()
            || string.Equals(_environment.EnvironmentName, "Demo", StringComparison.OrdinalIgnoreCase);

        return allowedEnvironment
            && _options.Enabled
            && !string.IsNullOrWhiteSpace(_options.ApiDemoActor)
            && !string.IsNullOrWhiteSpace(_options.ApiDemoActorHeader);
    }
}
