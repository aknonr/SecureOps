using FluentAssertions;
using SecureOps.Shared.Contracts.Sessions;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins how a session is described to an administrator.
/// </summary>
/// <remarks>
/// Every identity field on this contract is nullable, and the access model persists no display name
/// today — so the fallback chain is the normal path, not an edge case. What must never happen is a
/// fabricated person or a bare GUID standing in as identity on a screen where someone decides
/// whether to interrupt another operator's work.
/// </remarks>
public sealed class SessionViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Name_PrefersTheDisplayName()
    {
        SessionView.Name(Session(displayName: "Ayşe Yılmaz", principal: "CONTOSO\\ayilmaz"))
            .Should().Be("Ayşe Yılmaz");
    }

    [Fact]
    public void Name_FallsBackToThePrincipalWhenNoDisplayNameIsPersisted()
    {
        // The documented normal case: the access model stores no display name and the API returns
        // null rather than contacting AD or inventing one.
        SessionView.Name(Session(displayName: null, principal: "CONTOSO\\ayilmaz"))
            .Should().Be("CONTOSO\\ayilmaz");
    }

    [Fact]
    public void OpaqueOidcIdentity_IsNotUsedAsThePrimarySessionLabel()
    {
        ApplicationSessionResponse session = Session(
            displayName: null,
            principal: null,
            normalized: "oidc:opaque-stable-identity",
            provider: "oidc");

        SessionView.Name(session).Should().Be(SessionView.ProfilePending);
        SessionView.Name(session).Should().NotContain(session.NormalizedPrincipal!);
    }

    [Fact]
    public void Name_StatesThatIdentityIsMissingRatherThanShowingAnInternalId()
    {
        // The GUID is still available under technical details. It is not identity.
        ApplicationSessionResponse session = Session(displayName: null, principal: null, normalized: null);

        SessionView.Name(session).Should().Be(SessionView.UnknownUser);
        SessionView.Name(session).Should().NotContain(session.UserId.ToString());
    }

    [Fact]
    public void Account_IsOmittedWhenItWouldRepeatTheName()
    {
        // Without a display name the account is the name. Printing it twice reads as a fault.
        SessionView.Account(Session(displayName: null, principal: "CONTOSO\\ayilmaz"))
            .Should().BeNull();
    }

    [Fact]
    public void Account_IsShownUnderTheNameWhenItAddsSomething()
    {
        SessionView.Account(Session(displayName: "Ayşe Yılmaz", principal: "CONTOSO\\ayilmaz"))
            .Should().Be("CONTOSO\\ayilmaz");
    }

    [Fact]
    public void Authentication_PrefersTheProviderOverTheRawMethod()
    {
        SessionView.Authentication(Session(provider: "Windows Authentication"))
            .Should().Be("Windows Authentication");
    }

    [Fact]
    public void Authentication_FallsBackToTheMethodWhenNoProviderIsReported()
    {
        SessionView.Authentication(Session(provider: null)).Should().Be("Negotiate");
    }

    [Fact]
    public void Uid_IsTrimmedForSicilPresentation()
    {
        SessionView.Uid(Session(uid: " 12345 ")).Should().Be("12345");
    }

    [Fact]
    public void ExpiredSession_IsNotListedAsActive()
    {
        // A session past its absolute lifetime must not be offered with a "Sonlandır" button; the
        // command could only fail, and the failure would read as a fault in the page.
        SessionView.IsActive(Session(expiresIn: TimeSpan.FromMinutes(-1)), Now).Should().BeFalse();
        SessionView.IsActive(Session(expiresIn: TimeSpan.FromMinutes(1)), Now).Should().BeTrue();
    }

    [Theory]
    [InlineData(90, "1 sa 30 dk")]
    [InlineData(45, "45 dk")]
    [InlineData(-5, "Süresi doldu")]
    public void Remaining_IsStatedInPlainTerms(int minutes, string expected)
    {
        SessionView.Remaining(Session(expiresIn: TimeSpan.FromMinutes(minutes)), Now)
            .Should().Be(expected);
    }

    private static ApplicationSessionResponse Session(
        string? displayName = null,
        string? principal = "CONTOSO\\ayilmaz",
        string? normalized = "ayilmaz",
        string? provider = "Windows",
        TimeSpan? expiresIn = null,
        string? uid = null) =>
        new(
            SessionId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            UserId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            StartedAtUtc: Now.AddHours(-1),
            LastSeenAtUtc: Now.AddMinutes(-5),
            AbsoluteExpiresAtUtc: Now.Add(expiresIn ?? TimeSpan.FromHours(11)),
            AuthenticationMethod: "Negotiate",
            AccessVersion: 3,
            Principal: principal,
            NormalizedPrincipal: normalized,
            DisplayName: displayName,
            AuthenticationProvider: provider,
            IsCurrent: false,
            Uid: uid);
}
