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
    });

// No role or capability policies are registered here on purpose. Application authority lives in the
// API's access store and is read through GET /api/v1/access/me; a second, cookie-derived policy set
// in the UI could disagree with it. Pages require an authenticated user and gate their content on the
// capabilities the API reports, which keeps one source of truth for authorization.
builder.Services.AddAuthorization();

builder.Services.AddScoped<IDemoModeState, DemoModeState>();
builder.Services.AddScoped<ISignedInUserService, SignedInUserService>();
builder.Services.AddScoped<ICurrentAccessProvider, CurrentAccessProvider>();

builder.Services.AddTransient<DemoApiAuthHeaderHandler>();

builder.Services
    .AddHttpClient<IIdentityLookupApiClient, IdentityLookupApiClient>(ConfigureApiClient)
    .AddHttpMessageHandler<DemoApiAuthHeaderHandler>();

builder.Services
    .AddHttpClient<IAccessApiClient, AccessApiClient>(ConfigureApiClient)
    .AddHttpMessageHandler<DemoApiAuthHeaderHandler>();

builder.Services
    .AddHttpClient<IAccessAdminApiClient, AccessAdminApiClient>(ConfigureApiClient)
    .AddHttpMessageHandler<DemoApiAuthHeaderHandler>();

builder.Services
    .AddHttpClient<IOperationalRecordApiClient, OperationalRecordApiClient>(ConfigureApiClient)
    .AddHttpMessageHandler<DemoApiAuthHeaderHandler>();

builder.Services
    .AddHttpClient<IManagementReportingApiClient, ManagementReportingApiClient>(ConfigureApiClient)
    .AddHttpMessageHandler<DemoApiAuthHeaderHandler>();

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
app.MapGet(
    "/auth/sign-out",
    async (HttpContext httpContext) =>
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToTrusted(httpContext, "signed-out?provider=managed");
    })
    .AllowAnonymous();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

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
