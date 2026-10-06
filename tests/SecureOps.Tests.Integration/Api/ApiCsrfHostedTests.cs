using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SecureOps.Api.Middleware;
using SecureOps.Api.Security;
using SecureOps.Domain.Resources;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.Resources;

namespace SecureOps.Tests.Integration.Api;

public sealed class ApiCsrfHostedTests
{
    [Theory]
    [InlineData("POST", "/api/v1/resources/categories")]
    [InlineData("PUT", "/api/v1/resources/categories/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/api/v1/resources/categories/00000000-0000-0000-0000-000000000001")]
    [InlineData("PATCH", "/api/v1/service-accounts/transitions/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/v1/service-accounts/accounts/00000000-0000-0000-0000-000000000001/usage-scans")]
    public async Task CrossOrigin_Multipart_DeniedBeforeBindingAndAudited(string method, string path)
    {
        using WebApplicationFactory<Program> factory = Factory();
        using HttpClient client = Client(factory, intent: false);
        using HttpRequestMessage request = new(new HttpMethod(method), path + "?sentinel=never-log");
        request.Headers.Add("Origin", "https://evil.example");
        request.Content = new MultipartFormDataContent { { new StringContent("never-log-body"), "file", "synthetic.json" } };
        using HttpResponseMessage response = await client.SendAsync(request);
        await AssertProblem(response, HttpStatusCode.Forbidden, OperationalErrorCodes.ApiCsrfRejected);
        InMemoryAuditWriter audit = factory.Services.GetRequiredService<InMemoryAuditWriter>();
        AuditEvent denial = audit.Events.Single(item => item.Action == AuditActions.ApiCsrfRejected);
        denial.Actor.Should().Be("demo:platform-admin");
        string details = JsonSerializer.Serialize(denial.Details);
        details.Should().Contain("IntentHeaderMissingOrInvalid").And.NotContain("never-log").And.NotContain("evil.example")
            .And.NotContain("00000000-0000-0000-0000-000000000001");
        audit.Events.Should().NotContain(item => item.Action == AuditActions.AuthorizationDenied);
    }

    [Fact]
    public async Task AllowedOrigin_AndServerClient_WriteWhileRejectedRequestsDoNot()
    {
        using WebApplicationFactory<Program> factory = Factory();
        using HttpClient client = Client(factory);
        ResourceCategory[] before = (await client.GetFromJsonAsync<ResourceCategory[]>("/api/v1/resources/categories"))!;
        using HttpRequestMessage hostile = new(HttpMethod.Post, "/api/v1/resources/categories");
        hostile.Headers.Add("Origin", "https://evil.example");
        hostile.Content = JsonContent.Create(new SaveResourceCategoryRequest("Rejected synthetic category"));
        using HttpResponseMessage denied = await client.SendAsync(hostile);
        await AssertProblem(denied, HttpStatusCode.Forbidden, OperationalErrorCodes.ApiCsrfRejected);
        (await client.GetFromJsonAsync<ResourceCategory[]>("/api/v1/resources/categories"))!.Should().BeEquivalentTo(before);

        using HttpRequestMessage browser = new(HttpMethod.Post, "/api/v1/resources/categories");
        browser.Headers.Add("Origin", "https://trusted.example.invalid");
        browser.Headers.Add("Sec-Fetch-Site", "same-origin");
        browser.Content = JsonContent.Create(new SaveResourceCategoryRequest("Allowed synthetic category"));
        (await client.SendAsync(browser)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/v1/resources/categories", new SaveResourceCategoryRequest("Server synthetic category")))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<ResourceCategory[]>("/api/v1/resources/categories"))!.Should().HaveCount(before.Length + 2);
    }

    [Fact]
    public async Task CrossSite_AllowedOriginAndHeader_IsStillRejected()
    {
        using WebApplicationFactory<Program> factory = Factory();
        using HttpClient client = Client(factory);
        client.DefaultRequestHeaders.Add("Origin", "https://trusted.example.invalid");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/resources/categories", new SaveResourceCategoryRequest("Rejected"));
        await AssertProblem(response, HttpStatusCode.Forbidden, OperationalErrorCodes.ApiCsrfRejected);
        JsonSerializer.Serialize(factory.Services.GetRequiredService<InMemoryAuditWriter>().Events
            .Single(item => item.Action == AuditActions.ApiCsrfRejected).Details).Should().Contain("CrossSite");
    }

    [Fact]
    public async Task HeaderlessClient_GetWorksButUnsafeRequestFails()
    {
        using WebApplicationFactory<Program> factory = Factory();
        using HttpClient client = Client(factory, intent: false);
        client.DefaultRequestHeaders.Add("Origin", "https://evil.example");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        (await client.GetAsync("/api/v1/resources/categories")).StatusCode.Should().Be(HttpStatusCode.OK);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Remove("Sec-Fetch-Site");
        await AssertProblem(await client.PostAsJsonAsync("/api/v1/resources/categories", new SaveResourceCategoryRequest("Rejected")),
            HttpStatusCode.Forbidden, OperationalErrorCodes.ApiCsrfRejected);
    }

    [Fact]
    public async Task Authorization_DeniesBeforeCsrf_WhenCapabilityMissing()
    {
        using WebApplicationFactory<Program> factory = Factory();
        using HttpClient anonymous = factory.CreateClient();
        using HttpClient lead = Client(factory, intent: false, actor: "team-lead");
        (await anonymous.PostAsync("/api/v1/resources/categories", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using HttpResponseMessage response = await lead.PostAsync("/api/v1/resources/categories", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("stage").GetString().Should().Be("authorization");
        factory.Services.GetRequiredService<InMemoryAuditWriter>().Events.Should().NotContain(item => item.Action == AuditActions.ApiCsrfRejected);
    }

    [Theory]
    [InlineData(false, 403, "ApiCsrfRejected")]
    [InlineData(true, 503, "AuditStoreUnavailable")]
    public async Task Middleware_DenialOrAuditFailure_NeverReadsBodyOrCallsEndpoint(bool auditFails, int status, string code)
    {
        DefaultHttpContext context = new();
        context.Request.Method = "POST";
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "synthetic:operator")], "synthetic"));
        context.Request.Body = new UnreadableBody();
        context.Response.Body = new MemoryStream();
        IAuditWriter writer = Substitute.For<IAuditWriter>();
        if (auditFails)
        {
            writer.WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new InvalidOperationException("secret-sentinel")));
        }

        bool invoked = false;
        ApiCsrfMiddleware middleware = new(_ => { invoked = true; return Task.CompletedTask; },
            new ApiCsrfPolicy(new ConfigurationBuilder().Build()), NullLogger<ApiCsrfMiddleware>.Instance);
        await middleware.InvokeAsync(context, writer);
        invoked.Should().BeFalse();
        context.Response.StatusCode.Should().Be(status);
        context.Response.Body.Position = 0;
        string body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().Contain(code).And.NotContain("secret-sentinel");
        await writer.Received(1).WriteAsync(Arg.Is<AuditEvent>(item => item.Actor == "synthetic:operator" && item.Action == AuditActions.ApiCsrfRejected),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenApi_RouteInventory_AllUnsafeOperationsDeclareIntentHeader()
    {
        using WebApplicationFactory<Program> factory = Factory();
        using HttpClient client = Client(factory);
        JsonElement document = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        int unsafeCount = 0;
        foreach (JsonProperty path in document.GetProperty("paths").EnumerateObject())
        {
            foreach (JsonProperty operation in path.Value.EnumerateObject())
            {
                bool isSafe = ApiCsrfPolicy.IsSafeMethod(operation.Name.ToUpperInvariant());
                JsonElement[] headers = operation.Value.TryGetProperty("parameters", out JsonElement parameters)
                    ? parameters.EnumerateArray().Where(parameter => parameter.GetProperty("name").GetString() == ApiCsrf.HeaderName).ToArray() : [];
                if (isSafe)
                {
                    headers.Should().BeEmpty(path.Name);
                }
                else
                {
                    unsafeCount++;
                    headers.Should().ContainSingle(path.Name + " " + operation.Name);
                    headers[0].GetProperty("required").GetBoolean().Should().BeTrue();
                }
            }
        }

        RouteEndpoint[] routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToArray();
        int routedUnsafe = routes.Sum(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Count(method => !ApiCsrfPolicy.IsSafeMethod(method)) ?? 0);
        unsafeCount.Should().Be(routedUnsafe, "every routed unsafe API operation must publish the contract");
        unsafeCount.Should().BeGreaterThan(0);
        routes.Should().Contain(endpoint => endpoint.RoutePattern.RawText!.EndsWith("/usage-scans", StringComparison.Ordinal));
    }

    private static WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Test");
        builder.UseSetting("DemoAuth:Enabled", "true");
        builder.UseSetting("Access:DemoCompatibilityEnabled", "true");
        builder.UseSetting("Audit:Provider", "InMemory");
        builder.UseSetting("IdentityLookup:Provider", "Mock");
        builder.UseSetting("Swagger:Enabled", "true");
        builder.UseSetting("ServiceAccounts:Provider", "Disabled");
        builder.UseSetting("ApiCsrf:AllowedOrigins:0", "https://trusted.example.invalid");
    });

    private static HttpClient Client(WebApplicationFactory<Program> factory, bool intent = true, string actor = "platform-admin")
    {
        HttpClient client = intent ? factory.CreateApiClient() : factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", actor);
        return client;
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be(code);
        problem.GetProperty("correlationId").GetString().Should().NotBeNullOrEmpty();
    }

    private sealed class UnreadableBody : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Body must not be read");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
