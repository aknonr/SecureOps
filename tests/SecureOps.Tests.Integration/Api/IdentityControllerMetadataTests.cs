using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SecureOps.Api.Controllers;
using SecureOps.Api.Security;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Identity;

namespace SecureOps.Tests.Integration.Api;

public sealed class IdentityControllerMetadataTests
{
    [Fact]
    public void IdentityController_RequiresAuthentication()
    {
        AuthorizeAttribute? attribute = typeof(IdentityController).GetCustomAttribute<AuthorizeAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Policy.Should().BeNull();
    }

    [Fact]
    public void LookupAsync_IsPostEndpointRequiringIdentityLookupCapability()
    {
        MethodInfo? method = typeof(IdentityController).GetMethod(nameof(IdentityController.LookupAsync));

        method.Should().NotBeNull();
        HttpPostAttribute? attribute = method!.GetCustomAttribute<HttpPostAttribute>();
        attribute.Should().NotBeNull();
        attribute!.Template.Should().Be("lookup");

        AuthorizeAttribute? authorize = method!.GetCustomAttribute<AuthorizeAttribute>();
        authorize.Should().NotBeNull();
        authorize!.Policy.Should().Be(Policies.CanIdentityLookup);
    }

    [Fact]
    public void LookupAsync_HasIdentityLookupRateLimit()
    {
        MethodInfo method = typeof(IdentityController).GetMethod(nameof(IdentityController.LookupAsync))!;

        EnableRateLimitingAttribute? attribute = method.GetCustomAttribute<EnableRateLimitingAttribute>();

        attribute.Should().NotBeNull();
        attribute!.PolicyName.Should().Be(IdentityLookupRateLimits.Lookup);
    }

    [Fact]
    public void BulkLookupAsync_IsPostEndpointRequiringBulkCapabilityAndRateLimit()
    {
        MethodInfo method = typeof(IdentityController).GetMethod(nameof(IdentityController.BulkLookupAsync))!;

        method.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("bulk-lookup");
        method.GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(Policies.CanBulkIdentityLookup);
        method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.Should().Be(ApiRateLimits.BulkIdentityLookup);
    }

    [Fact]
    public void IdentityController_DoesNotExposeGetLookupByAccount()
    {
        string[] getTemplates = typeof(IdentityController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes<HttpGetAttribute>())
            .Select(attribute => attribute.Template ?? string.Empty)
            .ToArray();

        getTemplates.Should().BeEquivalentTo("me", "lookup/capabilities", "lookup/cache-diagnostics");
        getTemplates.Should().NotContain(template => template.Contains("{", StringComparison.Ordinal));
    }

    [Fact]
    public void IdentityLookupUserDto_ContainsOnlyApprovedResponseFields()
    {
        string[] fields = typeof(IdentityLookupUserDto)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        fields.Should().BeEquivalentTo(
            nameof(IdentityLookupUserDto.DisplayName),
            nameof(IdentityLookupUserDto.SamAccountName),
            nameof(IdentityLookupUserDto.UserPrincipalName),
            nameof(IdentityLookupUserDto.Mail),
            nameof(IdentityLookupUserDto.Department),
            nameof(IdentityLookupUserDto.Title),
            nameof(IdentityLookupUserDto.ManagerDisplayName),
            nameof(IdentityLookupUserDto.Enabled),
            nameof(IdentityLookupUserDto.Locked),
            nameof(IdentityLookupUserDto.AccountTypeEvidence));
    }
}
