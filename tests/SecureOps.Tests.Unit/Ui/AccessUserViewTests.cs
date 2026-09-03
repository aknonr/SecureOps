using SecureOps.Shared.Contracts.Access;
using SecureOps.Ui.Services;
using SecureOps.Ui.Shared.Components;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Covers the rejected-versus-pending distinction and profile fallback.
/// </summary>
/// <remarks>
/// The rejection cases are the important ones. The API keeps a refused user at
/// <c>AccessStatus.Pending</c> and creates no replacement request, so anything reading status alone
/// presents a closed decision as an open task. These tests pin the combination that separates them.
/// </remarks>
public sealed class AccessUserViewTests
{
    private static AccessRequestResponse Request(string status, DateTimeOffset? decidedAt = null) => new(
        Id: Guid.NewGuid(),
        UserId: Guid.NewGuid(),
        CorporateIdentity: "demo:team-lead",
        Status: status,
        RequestedAt: DateTimeOffset.UtcNow.AddHours(-2),
        DecidedAt: decidedAt,
        DecisionReason: null);

    private static AccessUserResponse User(
        string accessStatus,
        AccessRequestResponse? latest,
        AccessIdentityProfileResponse? profile = null) => new(
        UserId: Guid.NewGuid(),
        CorporateIdentity: "demo:team-lead",
        Profile: profile,
        AccessStatus: accessStatus,
        Roles: [],
        Capabilities: [],
        LatestRequest: latest,
        RequestHistory: latest is null ? [] : [latest],
        Version: 1,
        AuthenticationSource: "demo-api-bridge",
        FirstAuthenticatedAt: DateTimeOffset.UtcNow.AddDays(-1),
        LastAuthenticatedAt: DateTimeOffset.UtcNow,
        DisabledAt: null);

    [Fact]
    public void RejectedRequest_WithPendingUser_ReadsAsRejected()
    {
        AccessUserResponse user = User(
            AccessStatuses.Pending,
            Request(AccessRequestStatuses.Rejected, DateTimeOffset.UtcNow));

        Assert.True(AccessUserView.IsRejected(user));
        Assert.False(AccessUserView.IsAwaitingDecision(user));
        Assert.Equal("Reddedildi", AccessUserView.StatusLabel(user));
    }

    [Fact]
    public void RejectedUser_IsNotTonedLikeAnOpenTask()
    {
        AccessUserResponse user = User(
            AccessStatuses.Pending,
            Request(AccessRequestStatuses.Rejected, DateTimeOffset.UtcNow));

        // Caution is the pending tone. A closed refusal must not share it.
        Assert.Equal(SoStatusBadge.BadgeTone.Critical, AccessUserView.StatusTone(user));
    }

    [Fact]
    public void PendingRequest_WithPendingUser_ReadsAsAwaitingDecision()
    {
        AccessUserResponse user = User(AccessStatuses.Pending, Request(AccessRequestStatuses.Pending));

        Assert.True(AccessUserView.IsAwaitingDecision(user));
        Assert.False(AccessUserView.IsRejected(user));
        Assert.Equal("Onay bekliyor", AccessUserView.StatusLabel(user));
        Assert.Equal(SoStatusBadge.BadgeTone.Caution, AccessUserView.StatusTone(user));
    }

    [Fact]
    public void PendingUser_WithNoRequest_ReadsAsAwaitingDecision()
    {
        AccessUserResponse user = User(AccessStatuses.Pending, latest: null);

        Assert.True(AccessUserView.IsAwaitingDecision(user));
        Assert.False(AccessUserView.IsRejected(user));
    }

    [Fact]
    public void ApprovedUser_IsNeverRejected_EvenWithAnOlderRejection()
    {
        // Approved wins: a rejection earlier in the history says nothing about current access.
        AccessUserResponse user = User(
            AccessStatuses.Approved,
            Request(AccessRequestStatuses.Rejected, DateTimeOffset.UtcNow.AddDays(-3)));

        Assert.False(AccessUserView.IsRejected(user));
        Assert.True(AccessUserView.IsApproved(user));
        Assert.Equal("Onaylı", AccessUserView.StatusLabel(user));
    }

    [Fact]
    public void DisabledUser_ReadsAsDisabled_NotRejected()
    {
        AccessUserResponse user = User(
            AccessStatuses.Disabled,
            Request(AccessRequestStatuses.Rejected, DateTimeOffset.UtcNow));

        Assert.True(AccessUserView.IsDisabled(user));
        Assert.False(AccessUserView.IsRejected(user));
        Assert.Equal("Kapatıldı", AccessUserView.StatusLabel(user));
    }

    [Fact]
    public void EveryState_HasItsOwnIcon()
    {
        // State must not be carried by colour alone.
        string[] icons =
        [
            AccessUserView.StatusIcon(User(AccessStatuses.Approved, null)),
            AccessUserView.StatusIcon(User(AccessStatuses.Disabled, null)),
            AccessUserView.StatusIcon(User(AccessStatuses.Pending, Request(AccessRequestStatuses.Pending))),
            AccessUserView.StatusIcon(User(AccessStatuses.Pending, Request(AccessRequestStatuses.Rejected)))
        ];

        Assert.Equal(4, icons.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void StatusComparison_IsCaseInsensitive()
    {
        AccessUserResponse user = User("approved", null);

        Assert.True(AccessUserView.IsApproved(user));
    }
}

/// <summary>
/// Covers the rule that an unresolvable profile falls back to the principal, never to a placeholder.
/// </summary>
public sealed class AccessIdentityDisplayTests
{
    [Fact]
    public void Name_FallsBackToPrincipal_WhenProfileIsNull()
    {
        Assert.Equal("demo:team-lead", AccessIdentityDisplay.Name(null, "demo:team-lead"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_FallsBackToPrincipal_WhenDisplayNameIsAbsent(string? displayName)
    {
        var profile = new AccessIdentityProfileResponse(displayName, null, null, null, null);

        Assert.Equal("CONTOSO-user", AccessIdentityDisplay.Name(profile, "CONTOSO-user"));
    }

    [Fact]
    public void Name_UsesDisplayName_WhenResolved()
    {
        var profile = new AccessIdentityProfileResponse("Example Admin", null, null, null, null);

        Assert.Equal("Example Admin", AccessIdentityDisplay.Name(profile, "demo:platform-admin"));
    }

    [Fact]
    public void Name_UsesExactAccountBeforeOpaquePrincipal()
    {
        var profile = new AccessIdentityProfileResponse(null, "operator.one", null, null, null);

        Assert.Equal("operator.one", AccessIdentityDisplay.Name(profile, "oidc:opaque"));
        Assert.True(AccessIdentityDisplay.HasName(profile));
    }

    [Fact]
    public void HasName_IsFalse_WhenOnlyOtherFieldsResolved()
    {
        // A profile carrying an e-mail but no name must not suppress the identifier line.
        var profile = new AccessIdentityProfileResponse(null, null, "a@b.local", null, null);

        Assert.False(AccessIdentityDisplay.HasName(profile));
        Assert.True(AccessIdentityDisplay.HasAny(profile));
    }

    [Fact]
    public void HasAny_IsFalse_ForNullAndForAnAllBlankProfile()
    {
        Assert.False(AccessIdentityDisplay.HasAny(null));
        Assert.False(AccessIdentityDisplay.HasAny(
            new AccessIdentityProfileResponse(null, "  ", "", null, null)));
    }

    [Theory]
    [InlineData("demo:team-lead", "DL")]
    [InlineData("Example Admin", "EA")]
    [InlineData("platform-admin", "PA")]
    [InlineData("solo", "S")]
    public void Initials_DeriveFromWhateverNameIsShown(string source, string expected)
    {
        Assert.Equal(expected, AccessIdentityDisplay.Initials(null, source));
    }

    [Fact]
    public void Initials_SplitOnDomainSeparator()
    {
        Assert.Equal("CU", AccessIdentityDisplay.Initials(null, @"CONTOSO\user"));
    }

    [Fact]
    public void Initials_NeverThrowOnAwkwardInput()
    {
        Assert.Equal("?", AccessIdentityDisplay.Initials(null, "::"));
    }
}
