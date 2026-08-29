using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using SecureOps.Ui.Configuration;
using SecureOps.Ui.Hosting;
using SecureOps.Ui.Security;
using SecureOps.Ui.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

UiDataProtectionConfiguration.Validate(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddSecureOpsUiDataProtection(builder.Configuration);
builder.Services.AddHostedService<UiDataProtectionStartupValidationHostedService>();

// WebApplication.CreateBuilder auto-loads the build output's static web assets manifest only in the
// Development environment. When running locally in the Demo environment the manifest is otherwise
// skipped, so Razor Class Library assets such as _content/MudBlazor/MudBlazor.min.js and .css would
// 404 and MudBlazor's JS interop would tear down the Blazor circuit. Enable it for non-Development,
// non-Production hosts. Production publishes these assets into wwwroot and does not need it.
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsProduction())
{
    builder.WebHost.UseStaticWebAssets();
}

builder.Services.Configure<DemoModeOptions>(builder.Configuration.GetSection(DemoModeOptions.SectionName));

// Trusted HTTPS-offload recognition. Validated at startup so a misconfigured trust boundary stops
// the host rather than silently degrading to cleartext behaviour behind the load balancer.
builder.Services.AddSingleton<IValidateOptions<HttpsOffloadOptions>, HttpsOffloadOptionsValidator>();
builder.Services
    .AddOptions<HttpsOffloadOptions>()
    .Bind(builder.Configuration.GetSection(HttpsOffloadOptions.SectionName))
    .ValidateOnStart();

// Canonical SecureOps API base address with startup validation and a safe migration path from the
// legacy DemoMode:ApiBaseAddress key. Resolution runs at options-build time so the final merged
// configuration (appsettings + launch-profile environment variables) is honored.
builder.Services
    .AddOptions<IdentityLookupApiOptions>()
    .PostConfigure<IConfiguration>((options, configuration) =>
    {
        (string? value, bool usedLegacy) = IdentityLookupApiConfiguration.ResolveBaseAddress(configuration);
        options.BaseAddress = IdentityLookupApiConfiguration.NormalizeAbsolute(value);
        options.UsedLegacyKey = usedLegacy;

        int? timeoutSeconds = configuration.GetValue<int?>($"{IdentityLookupApiOptions.SectionName}:TimeoutSeconds");
        if (timeoutSeconds is > 0)
        {
            options.Timeout = TimeSpan.FromSeconds(timeoutSeconds.Value);
        }
    })
    .Validate(
        options => options.BaseAddress is { IsAbsoluteUri: true },
        $"{IdentityLookupApiOptions.SectionName}:BaseAddress must be an absolute URI ending with '/', "
            + "for example http://localhost:5000/.")
    .ValidateOnStart();

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddMudServices();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "__Host-SecureOpsUi.AntiForgery";
    options.Cookie.Path = "/";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.FormFieldName = "__RequestVerificationToken";
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-SecureOpsUi.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;

        // Every authenticated browser session carries a correlation value used to pick its API
        // cookie jar. Issuing it here rather than only at sign-in means a session established by
        // any current or future authentication path gets one, and a cookie predating this change
        // is upgraded on its next request instead of silently losing session reuse.
        //
        // It is a random value with no authority of its own, and it never leaves the server: the
        // browser only ever sees it inside this encrypted cookie.
        options.Events.OnValidatePrincipal = context =>
        {
            if (context.Principal?.Identity is ClaimsIdentity { IsAuthenticated: true } identity
                && identity.FindFirst(SignedInUserService.BrowserSessionClaim) is null)
            {
                identity.AddClaim(new Claim(
                    SignedInUserService.BrowserSessionClaim,
                    Guid.NewGuid().ToString("N")));

                context.ShouldRenew = true;
            }

            return Task.CompletedTask;
        };
    });

// No role or capability policies are registered here on purpose. Application authority lives in the
// API's access store and is read through GET /api/v1/access/me; a second, cookie-derived policy set
// in the UI could disagree with it. Pages require an authenticated user and gate their content on the
// capabilities the API reports, which keeps one source of truth for authorization.
builder.Services.AddAuthorization();

builder.Services.AddScoped<IDemoModeState, DemoModeState>();
builder.Services.AddScoped<ISignedInUserService, SignedInUserService>();
builder.Services.AddScoped<ICurrentAccessProvider, CurrentAccessProvider>();

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IApiSessionStore, ApiSessionStore>();
builder.Services.AddScoped<IApiSessionContext, ApiSessionContext>();

builder.Services.AddTransient<DemoApiAuthHeaderHandler>();
builder.Services.AddTransient<ApiSessionCookieHandler>();

// One transport strategy for every SecureOps API client. Registering them through a single helper
// is the point: the API's application session is a cookie, and a client that opted out of this
// pipeline would quietly start a second session for the same browser on every page load.
AddSecureOpsApiClient<IIdentityLookupApiClient, IdentityLookupApiClient>(builder.Services);
AddSecureOpsApiClient<IAccessApiClient, AccessApiClient>(builder.Services);
AddSecureOpsApiClient<IAccessAdminApiClient, AccessAdminApiClient>(builder.Services);
AddSecureOpsApiClient<IOperationalRecordApiClient, OperationalRecordApiClient>(builder.Services);
AddSecureOpsApiClient<IManagementReportingApiClient, ManagementReportingApiClient>(builder.Services);
AddSecureOpsApiClient<IDirectoryApiClient, DirectoryApiClient>(builder.Services);
AddSecureOpsApiClient<ISessionApiClient, SessionApiClient>(builder.Services);

WebApplication app = builder.Build();

// Surface the effective API base address once at startup (server-side only).
IdentityLookupApiOptions identityApiOptions = app.Services
    .GetRequiredService<IOptions<IdentityLookupApiOptions>>()
    .Value;
if (identityApiOptions.UsedLegacyKey)
{
    app.Logger.LogWarning(
        "SecureOps API base address resolved from legacy key '{LegacyKey}'. "
            + "Set '{CanonicalKey}:BaseAddress' instead. Effective base address: {BaseAddress}.",
        IdentityLookupApiOptions.LegacyBaseAddressKey,
        IdentityLookupApiOptions.SectionName,
        identityApiOptions.BaseAddress);
}
else
{
    app.Logger.LogInformation("SecureOps API base address: {BaseAddress}.", identityApiOptions.BaseAddress);
}

// The application-session cookie is __Host- prefixed, which means Secure, which means it is only
// ever sent over HTTPS. Against a cleartext API address the UI therefore cannot replay it, and the
// symptom is subtle: everything works, but every request opens another application session and the
// Aktif Oturumlar page fills with duplicates. Said out loud at startup so it is diagnosable.
if (identityApiOptions.BaseAddress is { IsAbsoluteUri: true } baseAddress
    && !string.Equals(baseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
{
    app.Logger.LogWarning(
        "SecureOps API base address {BaseAddress} is not HTTPS. The application-session cookie is "
            + "Secure, so it cannot be replayed over this scheme and each request will establish a "
            + "separate application session.",
        baseAddress);
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Runs immediately after forwarded headers and before anything that reads Request.IsHttps —
// HTTPS redirection, authentication redirects, secure cookies, and antiforgery. Behind the
// corporate load balancer TLS terminates upstream and the backend hop is cleartext, so without
// this the antiforgery system sees a non-SSL request and refuses to issue its Secure cookie.
//
// It does not trust X-Forwarded-Proto. The scheme is restored only when the immediate connection
// matches the whole configured trust boundary: trusted proxy IP, expected host, and expected local
// port. Disabled by default, and fails closed on any mismatch.
app.UseMiddleware<HttpsOffloadMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorPages();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", component = "SecureOps.Ui" }));

// Interim sign-in. A single action with no profile or role selection: the session establishes only
// who the operator is, and the API decides what they may do. When an identity provider is approved
// this endpoint becomes a challenge/callback pair and the login page keeps its shape.
app.MapPost(
    "/auth/sign-in",
    async (
        HttpContext httpContext,
        [FromForm] string? returnUrl,
        ISignedInUserService users,
        IDemoModeState shellMode) =>
    {
        if (!shellMode.MockAuthenticationEnabled)
        {
            return RedirectToTrusted(httpContext, "login?error=sign-in-disabled");
        }

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            users.CreateInterimPrincipal(),
            new AuthenticationProperties
            {
                IsPersistent = false,
                IssuedUtc = DateTimeOffset.UtcNow
            });

        return RedirectToReturnUrl(httpContext, returnUrl);
    })
    .AllowAnonymous();

// Sign-out lands on a page that confirms the outcome rather than back on the sign-in form, so a
// deliberate exit is never mistaken for a failed attempt. Anonymous because an already-lapsed session
// must still be able to complete a clean sign-out instead of being challenged.
//
// The API session is ended first, and deliberately so. Signing out of the UI only drops this
// application's cookie; the SecureOps application session lives in the API and would stay open,
// still listed on the Active Sessions page, until it idled out. Ending it first means "oturumu
// kapat" means what the operator thinks it means.
app.MapGet(
    "/auth/sign-out",
    async (
        HttpContext httpContext,
        IAccessApiClient accessApi,
        IApiSessionContext sessionContext,
        IApiSessionStore sessionStore,
        ILoggerFactory loggerFactory) =>
    {
        string? browserSessionKey = sessionContext.BrowserSessionKey;
        ILogger logger = loggerFactory.CreateLogger("SecureOps.Ui.SignOut");

        try
        {
            await accessApi.LogoutAsync(httpContext.RequestAborted);
        }
        catch (SecureOpsApiException ex)
        {
            // Reported, never surfaced. The operator asked to leave, and refusing to sign them out
            // because the API was unreachable would strand them signed in. Only the safe problem
            // code and correlation ID are recorded — no cookie, handle, or session identifier.
            logger.LogWarning(
                "API application-session logout did not complete. Code: {Code}. CorrelationId: {CorrelationId}.",
                ex.Problem.Code,
                ex.Problem.CorrelationId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "API application-session logout failed before UI sign-out.");
        }
        finally
        {
            // Dropped either way. If logout succeeded the handle is dead; if it failed, replaying it
            // for whoever signs in next on this browser would be worse than losing session reuse.
            if (browserSessionKey is not null)
            {
                sessionStore.Remove(browserSessionKey);
            }
        }

        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToTrusted(httpContext, "signed-out?provider=managed");
    })
    .AllowAnonymous();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

// Registers one SecureOps API client on the shared browser-session transport.
//
// UseCookies is off on purpose. A handler's own CookieContainer is not browser identity: the factory
// pools one handler chain per client name and shares it across every operator, so its container
// would both split one browser across several API sessions and mix separate browsers into one.
// ApiSessionCookieHandler replaces it with a jar chosen by the caller's browser session.
static IHttpClientBuilder AddSecureOpsApiClient<TClient, TImplementation>(IServiceCollection services)
    where TClient : class
    where TImplementation : class, TClient =>
    services
        .AddHttpClient<TClient, TImplementation>(ConfigureApiClient)
        .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler { UseCookies = false })
        .AddHttpMessageHandler<DemoApiAuthHeaderHandler>()
        .AddHttpMessageHandler<ApiSessionCookieHandler>();

static void ConfigureApiClient(IServiceProvider serviceProvider, HttpClient client)
{
    IdentityLookupApiOptions options = serviceProvider
        .GetRequiredService<IOptions<IdentityLookupApiOptions>>()
        .Value;

    client.BaseAddress = options.BaseAddress;
    client.Timeout = options.Timeout;
}

// Redirects to a caller-supplied path, which is sanitized because it originates from the request.
static IResult RedirectToReturnUrl(HttpContext httpContext, string? returnUrl) =>
    Results.Redirect(BuildAppPath(httpContext, LocalReturnUrl.Sanitize(returnUrl)));

// Redirects to a path chosen by this file. These are compile-time constants, never request input, so
// they bypass the sanitizer — which would otherwise reject the auth paths it is meant to protect.
static IResult RedirectToTrusted(HttpContext httpContext, string path) =>
    Results.Redirect(BuildAppPath(httpContext, path));

static string BuildAppPath(HttpContext httpContext, string relativePath)
{
    string pathBase = httpContext.Request.PathBase.HasValue
        ? httpContext.Request.PathBase.Value ?? string.Empty
        : string.Empty;

    return $"{pathBase}/{relativePath}";
}
