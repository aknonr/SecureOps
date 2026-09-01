using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.Access;

public sealed class OidcExternalIdentityNormalizerTests
{
    [Fact]
    public void CompletePrincipal_MapsReviewedClaimsWithoutGrantingRole()
    {
        OidcExternalIdentityNormalizer normalizer = Create();
        ClaimsPrincipal source = Principal(
            new("iss", "https://identity.example.test"),
            new("sub", "subject-100"),
            new("loginname", "operator.one"),
            new("displayname", "Operator One"),
            new("mail", "operator.one@example.test"),
            new("uid", "uid-100"),
            new("uygulama-role", "Administrator"),
            new("password", "must-not-survive"));

        OidcExternalIdentityNormalizationResult result = normalizer.Normalize(source);

        result.IsValid.Should().BeTrue();
        result.Identity!.AuthenticationProvider.Should().Be("OIDC");
        result.Identity.LoginName.Should().Be("operator.one");
        result.Principal!.Identity!.Name.Should().Be("operator.one");
        result.Principal.IsInRole("Administrator").Should().BeFalse();
        result.Principal.FindFirst(ExternalIdentityClaimTypes.RoleEvidence)!.Value.Should().Be("Administrator");
        result.Principal.Claims.Should().NotContain(claim => claim.Type == "password");
    }

    [Fact]
    public void IssuerAndSubject_ProduceStableOpaqueIdentifier()
    {
        OidcExternalIdentityNormalizer normalizer = Create();
        ClaimsPrincipal first = Principal(new("iss", "https://identity.example.test"), new("sub", "subject-100"));
        ClaimsPrincipal second = Principal(new("iss", "https://identity.example.test"), new("sub", "subject-100"));

        string firstId = normalizer.Normalize(first).Identity!.StableIdentifier;
        string secondId = normalizer.Normalize(second).Identity!.StableIdentifier;

        firstId.Should().Be(secondId).And.StartWith("oidc:");
        firstId.Should().NotContain("subject-100");
    }

    [Fact]
    public void MissingOptionalClaims_IsAccepted()
    {
        OidcExternalIdentityNormalizationResult result = Create().Normalize(
            Principal(new("iss", "https://identity.example.test"), new("sub", "subject-100")));

        result.IsValid.Should().BeTrue();
        result.Identity!.LoginName.Should().BeNull();
    }

    [Theory]
    [InlineData("iss")]
    [InlineData("sub")]
    public void MissingStableIdentityClaim_FailsClosed(string omitted)
    {
        Claim[] claims =
        [
            .. omitted == "iss" ? Array.Empty<Claim>() : [new Claim("iss", "https://identity.example.test")],
            .. omitted == "sub" ? Array.Empty<Claim>() : [new Claim("sub", "subject-100")]
        ];

        OidcExternalIdentityNormalizationResult result = Create().Normalize(Principal(claims));

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("OidcClaimsInvalid");
    }

    [Fact]
    public void DuplicateIdentityClaim_FailsClosed()
    {
        OidcExternalIdentityNormalizationResult result = Create().Normalize(Principal(
            new("iss", "https://identity.example.test"),
            new("sub", "subject-100"),
            new("loginname", "operator.one"),
            new("loginname", "operator.two")));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void OversizedOrExcessiveClaims_FailClosed()
    {
        OidcOptions options = new() { MaxClaimCount = 8, MaxClaimValueLength = 64 };
        OidcExternalIdentityNormalizer normalizer = new(Options.Create(options));
        ClaimsPrincipal tooMany = Principal(
            Enumerable.Range(0, 9).Select(index => new Claim($"claim-{index}", "value")).ToArray());
        ClaimsPrincipal tooLarge = Principal(
            new("iss", "https://identity.example.test"),
            new("sub", new string('x', 65)));

        normalizer.Normalize(tooMany).ErrorCode.Should().Be("OidcClaimsOutOfBounds");
        normalizer.Normalize(tooLarge).ErrorCode.Should().Be("OidcClaimsOutOfBounds");
    }

    [Fact]
    public void LoginName_IsTheServerSideJiraResolutionIdentity()
    {
        ClaimsPrincipal normalized = Create().Normalize(Principal(
            new("iss", "https://identity.example.test"),
            new("sub", "subject-100"),
            new("loginname", "operator.one"))).Principal!;

        normalized.Identity!.Name.Should().Be("operator.one");
        normalized.FindFirst(ExternalIdentityClaimTypes.LoginName)!.Value.Should().Be("operator.one");
    }

    private static OidcExternalIdentityNormalizer Create() => new(Options.Create(new OidcOptions()));

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "synthetic-oidc"));
}
