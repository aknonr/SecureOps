using FluentAssertions;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.Access;

namespace SecureOps.Tests.Unit.Access;

public sealed class AccessModuleViewTests
{
    private static readonly AccessActionDefinition[] _actions =
    [
        new("B.One", "Beta", "B1", "Beta one"),
        new("A.One", "Alpha", "A1", "Alpha one"),
        new("B.Two", "Beta", "B2", "Beta two")
    ];

    private static readonly AccessRoleDefinition[] _roles =
    [
        new("Viewer", "Viewer", "p", 1, false, ["B.One"]),
        new("Editor", "Editor", "p", 1, false, ["B.One", "B.Two"])
    ];

    [Fact]
    public void Overview_KeepsCatalogModuleOrderAndListsGrantingRoles()
    {
        AccessModuleOverviewResponse overview = AccessModuleView.Overview(_actions, _roles);

        overview.Modules.Select(module => module.Module).Should().Equal("Beta", "Alpha");
        overview.Modules[0].Actions.Select(action => action.Code).Should().Equal("B.One", "B.Two");
        overview.Modules[0].Actions[0].GrantedByRoles.Should().Equal("Editor", "Viewer");
        overview.Modules[1].Actions[0].GrantedByRoles.Should().BeEmpty();
    }

    [Fact]
    public void Explain_ApprovedUser_ExplainsGrantsOnlyThroughAssignedRoles()
    {
        AccessEffectiveResponse effective = AccessModuleView.Explain(User(AccessStatus.Approved, ["Viewer"], ["B.One"]), _actions, _roles);

        effective.GrantedCount.Should().Be(1);
        AccessEffectiveActionResponse one = effective.Modules[0].Actions[0];
        one.Granted.Should().BeTrue();
        one.ViaRoles.Should().Equal("Viewer");
        effective.Modules[0].Actions[1].Granted.Should().BeFalse();
    }

    [Theory]
    [InlineData(AccessStatus.Pending)]
    [InlineData(AccessStatus.Disabled)]
    public void Explain_NonApprovedUser_HasNothingGrantedEvenWithStoredCapabilities(AccessStatus status)
    {
        AccessEffectiveResponse effective = AccessModuleView.Explain(User(status, ["Editor"], ["B.One", "B.Two"]), _actions, _roles);

        effective.GrantedCount.Should().Be(0);
        effective.Modules.SelectMany(module => module.Actions).Should().OnlyContain(action => !action.Granted && action.ViaRoles.Count == 0);
    }

    [Fact]
    public void Explain_PersistedCapabilityOutsideCatalog_IsShownNotHidden()
    {
        AccessEffectiveResponse effective = AccessModuleView.Explain(User(AccessStatus.Approved, [], ["Legacy.Thing"]), _actions, _roles);

        AccessEffectiveModuleResponse other = effective.Modules.Single(module => module.Module == AccessModuleView.UncataloguedModule);
        other.Actions.Should().ContainSingle(action => action.Code == "Legacy.Thing" && action.Granted && action.ViaRoles.Count == 0);
        effective.GrantedCount.Should().Be(1);
    }

    private static ApplicationUser User(AccessStatus status, string[] roles, string[] capabilities) =>
        new(Guid.NewGuid(), "CONTOSO\\synthetic", "test", status, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, 1, roles, capabilities);
}
