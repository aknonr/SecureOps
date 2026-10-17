using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecureOps.Api.Security;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>
/// OLAY-2b: the pasted mail text must reach no log, audit record or error body. The application is hosted for real
/// (<c>Program</c>) with a log provider that records every message of every category and level; the request carries a marker
/// that must never appear anywhere. No SQL: the module is disabled in this host, so the checks cover the request pipeline
/// (authentication, CSRF, model binding, size limit, authorization, the service entry); the persistence path is not involved because
/// the endpoint has none.
/// </summary>
public sealed class ServiceAccountMailHintsApiTests
{
    private const string _marker = "OLAY2B-GIZLI-ICERIK-9f3a";
    private const string _route = "/api/v1/service-accounts/accounts/00000000-0000-0000-0000-0000000000a1/events/mail-hints";

    [Fact]
    public async Task TheText_ReachesNoLog_NoAudit_AndNoResponse_OnAnyPath()
    {
        CapturingLoggerProvider logs = new();
        using WebApplicationFactory<Program> factory = Factory(logs);
        await ApproveAsync(factory, "demo:platform-admin", "Admin");
        await ApproveAsync(factory, "demo:team-lead", "Lead");

        string valid = $"{{\"text\":\"Parola değişti {_marker} OCO-00000 parola: Abc12345!\"}}";
        string malformed = $"{{\"text\":\"{_marker} parola değişti\"";
        string wrongType = $"{{\"text\":{{\"{_marker}\":1}}}}";
        string oversize = $"{{\"text\":\"{_marker}{new string('x', 200 * 1024)}\"}}";
        List<string> responses = [];
        foreach (string actor in new[] { DemoApiAuthentication.PlatformAdminActor, DemoApiAuthentication.TeamLeadActor })
        {
            foreach (string body in new[] { valid, malformed, wrongType, oversize })
            {
                using HttpClient client = factory.CreateApiClient();
                client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", actor);
                using StringContent content = new(body, Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await client.PostAsync(_route, content, TestContext.Current.CancellationToken);
                ((int)response.StatusCode).Should().BeOneOf([400, 403, 413, 503], $"{actor}: the disabled module never answers 200");
                responses.Add(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }
        }

        // Without the CSRF intent header (a cross-origin form post) the request is refused before the body is read.
        using (HttpClient plain = factory.CreateClient())
        {
            plain.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", DemoApiAuthentication.PlatformAdminActor);
            plain.DefaultRequestHeaders.Add("Origin", "https://evil.example");
            using StringContent content = new(valid, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await plain.PostAsync(_route, content, TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            responses.Add(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        responses.Should().NotContain(r => r.Contains(_marker, StringComparison.Ordinal) || r.Contains("Abc12345", StringComparison.Ordinal));
        logs.Messages.Should().NotBeEmpty("the host logs something, so the capture is live");
        logs.Messages.Should().NotContain(m => m.Contains(_marker, StringComparison.Ordinal) || m.Contains("Abc12345", StringComparison.Ordinal)
            || m.Contains("Parola değişti", StringComparison.Ordinal));
        string audit = string.Join('\n', factory.Services.GetRequiredService<SecureOps.Infrastructure.Audit.InMemoryAuditWriter>().Events
            .Select(e => e.Action + System.Text.Json.JsonSerializer.Serialize(e.Details)));
        audit.Should().NotContain(_marker).And.NotContain("Abc12345");
    }

    [Fact]
    public async Task TheRoute_IsInTheContract_AsAProtectedPost_WithNoTextInTheExamples()
    {
        using WebApplicationFactory<Program> factory = Factory(new CapturingLoggerProvider(), swagger: true);
        await ApproveAsync(factory, "demo:platform-admin", "Admin");
        using HttpClient client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", DemoApiAuthentication.PlatformAdminActor);
        string json = await client.GetStringAsync("/swagger/v1/swagger.json", TestContext.Current.CancellationToken);
        json.Should().Contain("/api/v1/service-accounts/accounts/{id}/events/mail-hints").And.Contain("MailHintsRequest").And.Contain("MailHints");
    }

    private static WebApplicationFactory<Program> Factory(CapturingLoggerProvider logs, bool swagger = false) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("Swagger:Enabled", swagger ? "true" : "false");
            builder.UseSetting("DemoAuth:Enabled", "true");
            builder.UseSetting("DemoAuth:HeaderName", "X-SecureOps-Demo-Actor");
            builder.UseSetting("Access:DemoCompatibilityEnabled", "false");
            builder.UseSetting("Audit:Provider", "InMemory");
            builder.UseSetting("IdentityLookup:Provider", "Mock");
            builder.UseSetting("ServiceAccounts:Provider", "Disabled");
            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(logs);
            });
        });

    private static async Task ApproveAsync(WebApplicationFactory<Program> factory, string identity, string role)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IAccessRepository repository = scope.ServiceProvider.GetRequiredService<IAccessRepository>();
        EnsureAccessUserResult ensured = await repository.EnsureUserAsync(new CorporatePrincipal(identity, "demo-api-bridge"), true, TimeSpan.FromMinutes(5),
            CancellationToken.None);
        (await repository.DecideRequestAsync(ensured.PendingRequest!.Id, AccessRequestStatus.Approved, ensured.PendingRequest.Version, "system:test-seed",
            [role], "Synthetic platform role fixture.", CancellationToken.None)).Disposition.Should().Be(AccessMutationDisposition.Applied);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Capture(Messages, categoryName);

        public void Dispose()
        {
        }

        private sealed class Capture(ConcurrentQueue<string> sink, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                sink.Enqueue($"{category}|{logLevel}|{formatter(state, exception)}|{exception}");
        }
    }
}
