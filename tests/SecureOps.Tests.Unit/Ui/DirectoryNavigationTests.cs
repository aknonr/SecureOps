using FluentAssertions;
using SecureOps.Shared.Contracts.Directory;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins how directory objects are identified and linked.
/// </summary>
/// <remarks>
/// Two separate operational defects are covered here. Navigating by display text asked the server
/// about a string rather than about the object the operator was looking at, which fails outright
/// when a group's name and its <c>sAMAccountName</c> differ. And navigating in component state left
/// no history, so Back from a group threw away the account lookup that led there.
/// </remarks>
public sealed class DirectoryNavigationTests
{
    [Fact]
    public void GroupNavigation_UsesTheServerLookupKeyRatherThanDisplayText()
    {
        // Name and sAMAccountName differ, which is the case display-text navigation gets wrong.
        DirectoryGroupSummaryDto group = Summary(
            name: "Ornek Operasyon Ekibi",
            samAccountName: "GRP-ORNEK-OPERASYON",
            lookupKey: "GRP-ORNEK-OPERASYON-CANONICAL");

        DirectoryView.ExactGroupIdentifier(group).Should().Be("GRP-ORNEK-OPERASYON-CANONICAL");
    }

    [Fact]
    public void GroupNavigation_FallsBackToAccountNameWhenNoLookupKeyIsSupplied()
    {
        // lookupKey is additive and nullable; the account name remains the next most exact value.
        DirectoryGroupSummaryDto group = Summary(
            name: "Ornek Operasyon Ekibi",
            samAccountName: "GRP-ORNEK-OPERASYON",
            lookupKey: null);

        DirectoryView.ExactGroupIdentifier(group).Should().Be("GRP-ORNEK-OPERASYON");
    }

    [Fact]
    public void GroupNavigation_NeverSendsADistinguishedName()
    {
        // The endpoints reject raw DNs, and a DN is a filter rather than exact input.
        DirectoryGroupSummaryDto group = Summary(
            name: null,
            samAccountName: null,
            lookupKey: null,
            distinguishedName: "CN=Ornek,OU=Groups,DC=contoso,DC=local");

        DirectoryView.ExactGroupIdentifier(group).Should().BeNull();
    }

    [Fact]
    public void ResolvedGroup_IsFollowedByItsLookupKeyNotTheTypedQuery()
    {
        // After the overview resolves, member, analysis, and export calls must stop replaying what
        // the operator typed: it found the group once, but it does not identify it.
        DirectoryGroupDetailDto detail = new(
            StableIdentifier: "S-1-5-21-1",
            Name: "Ornek Operasyon Ekibi",
            SamAccountName: "GRP-ORNEK-OPERASYON",
            DistinguishedName: "CN=Ornek,OU=Groups,DC=contoso,DC=local",
            Description: null,
            Category: "Security",
            Scope: "Global",
            ManagedBy: null,
            DirectMemberCount: 4,
            LookupKey: "GRP-ORNEK-OPERASYON");

        DirectoryView.ExactGroupIdentifier(detail).Should().Be("GRP-ORNEK-OPERASYON");
    }

    [Fact]
    public void MemberNavigation_PrefersTheLookupKey()
    {
        DirectoryMemberDto member = new(
            StableIdentifier: "S-1-5-21-2",
            Name: "Ayse Yilmaz",
            SamAccountName: "ayilmaz",
            DistinguishedName: "CN=Ayse,OU=Users,DC=contoso,DC=local",
            MemberType: "User",
            LookupKey: "ayilmaz-canonical");

        DirectoryView.ExactMemberIdentifier(member).Should().Be("ayilmaz-canonical");
    }

    [Fact]
    public void GroupLink_IsAnAddressableUrl()
    {
        DirectoryRoutes.Group("GRP-ORNEK-OPERASYON")
            .Should().Be("directory/groups?group=GRP-ORNEK-OPERASYON");
    }

    [Fact]
    public void UserLink_CarriesTheAccountAndTheOpenTab()
    {
        // The tab travels with the account so Back from a group restores the membership list the
        // operator left, not the summary tab.
        DirectoryRoutes.User("kullanici01", "groups")
            .Should().Be("directory/users?account=kullanici01&tab=groups");
    }

    [Fact]
    public void Links_EscapeValuesThatWouldOtherwiseBreakTheQueryString()
    {
        DirectoryRoutes.User("CONTOSO\\kullanici01")
            .Should().Be("directory/users?account=CONTOSO%5Ckullanici01");
    }

    [Fact]
    public void Links_DegradeToTheBarePageWhenNoExactIdentifierExists()
    {
        // Better than a link that asks the server about an empty string.
        DirectoryRoutes.Group(null).Should().Be(DirectoryRoutes.GroupsPath);
        DirectoryRoutes.User("   ").Should().Be(DirectoryRoutes.UsersPath);
    }

    private static DirectoryGroupSummaryDto Summary(
        string? name,
        string? samAccountName,
        string? lookupKey,
        string? distinguishedName = null) =>
        new(
            StableIdentifier: "S-1-5-21-1",
            Name: name,
            SamAccountName: samAccountName,
            DistinguishedName: distinguishedName,
            Description: null,
            Category: "Security",
            Scope: "Global",
            MembershipKind: "Direct",
            LookupKey: lookupKey);
}
