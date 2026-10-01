using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Integration.Api;

public sealed class CorporateJiraSocketTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_LostResponseAfterBody_DoesNotResendToJira(bool reuseConnection)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using WebApplication app = builder.Build();
        int creates = 0;
        string? received = null;
        app.MapGet("/warmup", () => "synthetic");
        app.MapPost("/rest/api/2/issue", async context =>
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            received = await reader.ReadToEndAsync(context.RequestAborted);
            Interlocked.Increment(ref creates);
            context.Abort();
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await app.StartAsync(deadline.Token);
        try
        {
            string address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 20
            };
            using var http = new HttpClient(handler) { BaseAddress = new Uri(address), Timeout = Timeout.InfiniteTimeSpan };
            if (reuseConnection)
            {
                using HttpResponseMessage warmup = await http.GetAsync("/warmup", deadline.Token);
                warmup.StatusCode.Should().Be(HttpStatusCode.OK);
            }
            var client = new CorporateJiraClient(http,
                Options.Create(new JiraIntegrationOptions { RequestTimeoutSeconds = 5 }),
                Options.Create(new OperationalRecordsOptions()), new EnterpriseIntegrationHealthState(),
                new EnterpriseIntegrationTelemetry(), NullLogger<CorporateJiraClient>.Instance);
            var draft = new JiraIssueDraft(Guid.NewGuid(), "OR-100", "SYN", "Task", "Synthetic socket check",
                "No corporate destination", "synthetic.requester", "synthetic-v1", new string('a', 64), [],
                new("3", "customfield_12700", "Synthetic team", "customfield_11500", ["SunucuTalep"]))
            { RequestType = OperationalRecordClassification.ServerRequest };

            Func<Task> create = () => client.CreateIssueAsync(draft, deadline.Token);
            ExternalIntegrationException failure = (await create.Should().ThrowAsync<ExternalIntegrationException>()).Which;

            failure.OutcomeUnknown.Should().BeTrue();
            failure.Retryable.Should().BeFalse();
            creates.Should().Be(1, "a lost response must not dispatch the reviewed JSON body twice");
            received.Should().Contain("Synthetic socket check");
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }
}
