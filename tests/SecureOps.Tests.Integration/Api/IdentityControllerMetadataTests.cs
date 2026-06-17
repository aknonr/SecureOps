using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Api.Controllers;
using SecureOps.Shared.Auth;

namespace SecureOps.Tests.Integration.Api;

public sealed class IdentityControllerMetadataTests
{
    [Fact]
    public void IdentityController_RequiresTeamLeadOrAbove()
    {
        AuthorizeAttribute? attribute = typeof(IdentityController).GetCustomAttribute<AuthorizeAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Policy.Should().Be(Policies.TeamLeadOrAbove);
    }

    [Fact]
    public void LookupAsync_IsPostEndpoint()
    {
        MethodInfo? method = typeof(IdentityController).GetMethod(nameof(IdentityController.LookupAsync));

        method.Should().NotBeNull();
        HttpPostAttribute? attribute = method!.GetCustomAttribute<HttpPostAttribute>();
        attribute.Should().NotBeNull();
        attribute!.Template.Should().Be("lookup");
    }
}
