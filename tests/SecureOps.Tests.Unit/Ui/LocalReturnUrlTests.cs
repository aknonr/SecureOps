using FluentAssertions;
using SecureOps.Ui.Security;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Verifies the post-sign-in return path cannot leave the application or re-enter the auth flow.
/// </summary>
public sealed class LocalReturnUrlTests
{
    [Theory]
    [InlineData("identity-lookup", "identity-lookup")]
    [InlineData("/identity-lookup", "identity-lookup")]
    [InlineData("access/me", "access/me")]
    [InlineData("account", "account")]
    public void Sanitize_KeepsLocalApplicationPaths(string input, string expected)
    {
        LocalReturnUrl.Sanitize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example/steal")]
    [InlineData("http://localhost:5000/api/v1/identity/lookup")]
    [InlineData("\\\\fileserver\\share")]
    [InlineData("javascript:alert(1)")]
    public void Sanitize_RejectsAnythingThatLeavesTheApplication(string input)
    {
        LocalReturnUrl.Sanitize(input).Should().Be(LocalReturnUrl.DefaultPath);
    }

    [Theory]
    [InlineData("auth/sign-out")]
    [InlineData("/auth/sign-out")]
    [InlineData("auth/sign-in")]
    [InlineData("AUTH/SIGN-OUT")]
    [InlineData("demo-auth/sign-in")]
    public void Sanitize_RejectsAuthEndpoints(string input)
    {
        // Without this a returnUrl of auth/sign-out would sign the operator straight back out after a
        // successful sign-in.
        LocalReturnUrl.Sanitize(input).Should().Be(LocalReturnUrl.DefaultPath);
    }

    [Theory]
    [InlineData("login")]
    [InlineData("login?returnUrl=x")]
    [InlineData("signed-out")]
    [InlineData("session-expired")]
    public void Sanitize_RejectsAuthPagesThatWouldLoop(string input)
    {
        LocalReturnUrl.Sanitize(input).Should().Be(LocalReturnUrl.DefaultPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    public void Sanitize_FallsBackWhenThereIsNoUsablePath(string? input)
    {
        LocalReturnUrl.Sanitize(input).Should().Be(LocalReturnUrl.DefaultPath);
    }

    [Fact]
    public void Sanitize_RejectsOverlongAndControlCharacterInput()
    {
        LocalReturnUrl.Sanitize(new string('a', 257)).Should().Be(LocalReturnUrl.DefaultPath);
        LocalReturnUrl.Sanitize("path\r\nSet-Cookie: x").Should().Be(LocalReturnUrl.DefaultPath);
    }

    [Fact]
    public void Sanitize_DoesNotBlockPathsThatMerelyStartWithABlockedWord()
    {
        // "authorization-report" is a legitimate route even though it begins with "auth".
        LocalReturnUrl.Sanitize("authorization-report").Should().Be("authorization-report");
    }

    [Fact]
    public void Sanitize_UsesTheSuppliedFallback()
    {
        LocalReturnUrl.Sanitize(null, "account").Should().Be("account");
        LocalReturnUrl.Sanitize("https://evil.example", "account").Should().Be("account");
    }
}
