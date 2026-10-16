using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SecureOps.Api.Security;

namespace SecureOps.Tests.Integration.Api;

public sealed class IdentityRateLimitPartitionTests
{
    [Fact]
    public void GetPartitionKey_UsesAuthenticatedUserAndOperation()
    {
        DefaultHttpContext context = CreateContext("CONTOSO\\lead.one", "192.0.2.10");

        string partitionKey = IdentityLookupRateLimits.GetPartitionKey(context);

        partitionKey.Should().Be("contoso\\lead.one|IdentityLookup");
        partitionKey.Should().NotContain("192.0.2.10");
    }

    [Fact]
    public void GetPartitionKey_DiffersForDifferentAuthenticatedUsersOnSameIp()
    {
        DefaultHttpContext first = CreateContext("CONTOSO\\lead.one", "192.0.2.10");
        DefaultHttpContext second = CreateContext("CONTOSO\\lead.two", "192.0.2.10");

        IdentityLookupRateLimits.GetPartitionKey(first)
            .Should().NotBe(IdentityLookupRateLimits.GetPartitionKey(second));
    }

    private static DefaultHttpContext CreateContext(string userName, string ip)
    {
        DefaultHttpContext context = new();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/v1/identity/lookup";
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, userName)],
            authenticationType: "TestAuth"));
        return context;
    }
}
