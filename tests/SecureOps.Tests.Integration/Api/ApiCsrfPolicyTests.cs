using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using SecureOps.Api.Security;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Tests.Integration.Api;

public sealed class ApiCsrfPolicyTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("https://trusted.example.invalid/unsafe")]
    [InlineData("https://trusted.example.invalid/")]
    [InlineData("https://trusted.example.invalid?secret=sentinel")]
    [InlineData("https://trusted.example.invalid#fragment")]
    [InlineData("https://user:sentinel@trusted.example.invalid")]
    [InlineData("https://*.example.invalid")]
    [InlineData("https://trusted.example.invalid https://evil.example")]
    [InlineData("https://trusted.example.invalid,https://evil.example")]
    [InlineData("https://trusted.example.invalid\\evil")]
    [InlineData("ftp://trusted.example.invalid")]
    [InlineData("")]
    public void Configuration_InvalidOrigin_StopsStartupWithoutEchoingValue(string origin)
    {
        IConfiguration config = Config(origin);
        Action create = () => _ = new ApiCsrfPolicy(config);
        create.Should().Throw<InvalidOperationException>().WithMessage("ApiCsrf:AllowedOrigins must contain exact HTTP(S) origins*");
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public void SafeMethod_HostileHeaders_IsUnaffected(string method)
    {
        DefaultHttpContext context = Context(method);
        context.Request.Headers.Origin = "null";
        context.Request.Headers["Sec-Fetch-Site"] = "cross-site";
        new ApiCsrfPolicy(Config()).RejectionReason(context.Request).Should().BeNull();
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("TRACE")]
    [InlineData("CUSTOM")]
    public void UnsafeMethod_NoHeader_IsDenied(string method) =>
        new ApiCsrfPolicy(Config()).RejectionReason(Context(method).Request).Should().Be("IntentHeaderMissingOrInvalid");

    [Theory]
    [InlineData("Origin", "https://trusted.example.invalid", null)]
    [InlineData("Origin", "https://TRUSTED.example.invalid", null)]
    [InlineData("Origin", "http://trusted.example.invalid", "SourceOriginNotAllowed")]
    [InlineData("Origin", "https://trusted.example.invalid:8443", "SourceOriginNotAllowed")]
    [InlineData("Origin", "https://trusted.example.invalid.evil.example", "SourceOriginNotAllowed")]
    [InlineData("Origin", "null", "SourceOriginInvalid")]
    [InlineData("Origin", "https://trusted.example.invalid/path", "SourceOriginInvalid")]
    [InlineData("Referer", "https://trusted.example.invalid/page?secret=sentinel", null)]
    [InlineData("Referer", "https://evil.example/page", "SourceOriginNotAllowed")]
    public void SourceHeader_ExactConfiguredOrigin_IsRequired(string header, string value, string? reason)
    {
        DefaultHttpContext context = Context("POST", intent: true);
        context.Request.Headers[header] = value;
        new ApiCsrfPolicy(Config("https://trusted.example.invalid")).RejectionReason(context.Request).Should().Be(reason);
    }

    [Fact]
    public void AmbiguousHeaders_TrustedFallback_DoesNotRescueOrigin()
    {
        DefaultHttpContext context = Context("POST", intent: true);
        ApiCsrfPolicy policy = new(Config("https://trusted.example.invalid"));
        context.Request.Headers.Referer = "https://trusted.example.invalid/page";
        context.Request.Headers.Origin = new[] { "https://trusted.example.invalid", "https://evil.example" };
        policy.RejectionReason(context.Request).Should().Be("SourceOriginInvalid");
        context.Request.Headers.Origin = "null";
        policy.RejectionReason(context.Request).Should().Be("SourceOriginInvalid");
        context.Request.Headers.Remove("Origin");
        context.Request.Headers[ApiCsrf.HeaderName] = new[] { "1", "1" };
        policy.RejectionReason(context.Request).Should().Be("IntentHeaderMissingOrInvalid");
    }

    [Theory]
    [InlineData("cross-site", "CrossSite")]
    [InlineData("same-origin", null)]
    [InlineData("same-site", null)]
    [InlineData("none", null)]
    [InlineData("same-origin,cross-site", "FetchMetadataInvalid")]
    [InlineData("invalid", "FetchMetadataInvalid")]
    [InlineData("", "FetchMetadataInvalid")]
    public void FetchMetadata_CustomHeaderAndAllowedOrigin_DoNotBypassCrossSite(string site, string? reason)
    {
        DefaultHttpContext context = Context("POST", intent: true);
        context.Request.Headers.Origin = "https://trusted.example.invalid";
        context.Request.Headers["Sec-Fetch-Site"] = site;
        new ApiCsrfPolicy(Config("https://trusted.example.invalid")).RejectionReason(context.Request).Should().Be(reason);
    }

    [Fact]
    public void NoOrigin_ExplicitServerClient_PassesButBrowserMetadataFails()
    {
        DefaultHttpContext context = Context("POST", intent: true);
        ApiCsrfPolicy policy = new(Config());
        policy.RejectionReason(context.Request).Should().BeNull();
        context.Request.Headers["Sec-Fetch-Site"] = "same-origin";
        policy.RejectionReason(context.Request).Should().Be("SourceOriginMissing");
        context.Request.Headers.Remove("Sec-Fetch-Site");
        context.Request.Headers["Sec-Fetch-Mode"] = "navigate";
        policy.RejectionReason(context.Request).Should().Be("SourceOriginMissing");
        context.Request.Headers.Origin = "https://trusted.example.invalid";
        policy.RejectionReason(context.Request).Should().Be("SourceOriginNotAllowed", "missing configuration must deny browser origins");
    }

    private static IConfiguration Config(string? origin = null) => new ConfigurationBuilder()
        .AddInMemoryCollection(origin is null ? [] : new Dictionary<string, string?> { ["ApiCsrf:AllowedOrigins:0"] = origin }).Build();

    private static DefaultHttpContext Context(string method, bool intent = false)
    {
        DefaultHttpContext context = new();
        context.Request.Method = method;
        if (intent)
        {
            context.Request.Headers[ApiCsrf.HeaderName] = ApiCsrf.HeaderValue;
        }

        return context;
    }
}
