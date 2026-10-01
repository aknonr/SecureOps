using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SecureOps.Domain.Access;
using SecureOps.Domain.Resources;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Resources;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Resources;

namespace SecureOps.Tests.Unit.Resources;

public sealed partial class ResourceCatalogueTests
{
    [Theory]
    [InlineData("cards", "comfortable", 25, true)]
    [InlineData("list", "compact", 100, true)]
    [InlineData("html", "comfortable", 25, false)]
    [InlineData("cards", "tiny", 25, false)]
    [InlineData("list", "compact", 1000, false)]
    public void Layout_UsesClosedPresentationVocabulary(string view, string density, int size, bool valid) =>
        ResourceWorkspacePolicy.Valid(new(view, density, size, ["links", "groups"])).Should().Be(valid);

    [Fact]
    public void Layout_RejectsArbitraryRoutesDuplicatesAndNulls()
    {
        ResourceWorkspacePolicy.Valid(null).Should().BeFalse();
        ResourceWorkspacePolicy.Valid(new("cards", "compact", 25, null!)).Should().BeFalse();
        ResourceWorkspacePolicy.Valid(new("cards", "compact", 25, ["links", "links"])).Should().BeFalse();
        ResourceWorkspacePolicy.Valid(new("cards", "compact", 25, ["admin/users"])).Should().BeFalse();
        ResourceWorkspacePolicy.Valid(new("cards", "compact", 25, [null!])).Should().BeFalse();
    }

    [Fact]
    public void LegacyJson_DefaultsWithoutInventingMembership()
    {
        ResourcePreferences legacy = JsonSerializer.Deserialize<ResourcePreferences>(
            """{"Version":3,"FavouriteIds":[],"Sets":[],"DefaultSetId":null}""")!;
        legacy.WorkspaceLayout.Should().BeNull();
        ResourceWorkspacePolicy.Project(legacy.WorkspaceLayout, AccessRoleCatalog.GetCapabilities(["Operator"]))
            .Should().BeEquivalentTo(ResourceWorkspaceLayout.Default);
        legacy.Version.Should().Be(3);
    }

    [Fact]
    public async Task Layout_PreservesHiddenMembershipRejectsStaleVersionAndIsOwnerScoped()
    {
        var repository = new InMemoryResourceRepository(new InMemoryAuditWriter());
        Guid owner = Guid.NewGuid(), hidden = Guid.NewGuid(), group = Guid.NewGuid();
        var original = new ResourcePreferences(0, [hidden], [new(group, "Synthetic group", [hidden])], group, true);
        await repository.SavePreferencesAsync(original, 0, new(owner, "synthetic"), _token);
        ResourceCatalogueService service = WorkspaceService(repository, owner, "Operator");
        var layout = new ResourceWorkspaceLayout("list", "compact", 10, ["groups", "links"]);
        ResourcePreferencesResponse response = (await service.SaveLayoutAsync(_principal, _context, new(layout, 1), _token)).Value!;
        response.Version.Should().Be(2);
        response.WorkspaceLayout.Should().BeEquivalentTo(layout);
        response.Favourites.Should().BeEmpty();
        response.Sets.Single().Links.Should().BeEmpty();
        ResourcePreferences saved = await repository.PreferencesAsync(owner, _token);
        saved.FavouriteIds.Should().Equal(hidden);
        saved.Sets.Single().LinkIds.Should().Equal(hidden);
        saved.DefaultSetId.Should().Be(group);
        saved.GuideDismissed.Should().BeTrue();
        (await service.SaveLayoutAsync(_principal, _context, new(ResourceWorkspaceLayout.Default, 1), _token))
            .ErrorCode.Should().Be(ResourceErrors.Conflict);
        (await repository.PreferencesAsync(owner, _token)).Version.Should().Be(2);
        ResourceCatalogueService otherAdmin = WorkspaceService(repository, Guid.NewGuid(), "Admin");
        (await otherAdmin.PreferencesAsync(_principal, _context, _token)).Value!.Version.Should().Be(0);
        (await otherAdmin.SaveLayoutAsync(_principal, _context, new(layout, 2), _token)).ErrorCode.Should().Be(ResourceErrors.Conflict);
        ResourcePreferencesResponse reset = (await service.SaveLayoutAsync(_principal, _context, new(ResourceWorkspaceLayout.Default, 2), _token)).Value!;
        reset.WorkspaceLayout.Should().BeEquivalentTo(ResourceWorkspaceLayout.Default);
        (await repository.PreferencesAsync(owner, _token)).FavouriteIds.Should().Equal(hidden);
    }

    [Fact]
    public async Task Layout_RevalidatesCapabilitiesOnReadAndSaveWithoutMutatingOnRead()
    {
        var repository = new InMemoryResourceRepository(new InMemoryAuditWriter());
        var owner = Guid.NewGuid();
        var layout = new ResourceWorkspaceLayout("cards", "comfortable", 25, ["catalogue", "links"]);
        await WorkspaceService(repository, owner, "Admin").SaveLayoutAsync(_principal, _context, new(layout, 0), _token);
        ResourceCatalogueService ordinary = WorkspaceService(repository, owner, "Operator");
        ResourcePreferencesResponse response = (await ordinary.PreferencesAsync(_principal, _context, _token)).Value!;
        response.WorkspaceLayout!.Shortcuts.Should().Equal("links");
        (await repository.PreferencesAsync(owner, _token)).WorkspaceLayout!.Shortcuts.Should().Equal("catalogue", "links");
        (await ordinary.SaveLayoutAsync(_principal, _context, new(layout, 1), _token)).ErrorCode.Should().Be("AccessDenied");
    }

    [Fact]
    public async Task Resolution_IsBoundedOrderedAndFiltersUnavailableLinks()
    {
        var repository = new InMemoryResourceRepository(new InMemoryAuditWriter());
        ResourceCatalogueService admin = WorkspaceService(repository, Guid.NewGuid(), "Admin");
        ResourceCategory category = (await admin.SaveCategoryAsync(_principal, _context, Guid.Empty, new("Synthetic"), true, _token)).Value!;
        ResourceLink first = (await admin.SaveLinkAsync(_principal, _context, Guid.Empty,
            new(category.Id, "First", "https://example.invalid/first", "Synthetic"), true, _token)).Value!;
        ResourceLink last = (await admin.SaveLinkAsync(_principal, _context, Guid.Empty,
            new(category.Id, "Last", "https://example.invalid/last", "Synthetic"), true, _token)).Value!;
        ResourceCatalogueService ordinary = WorkspaceService(repository, Guid.NewGuid(), "Operator");
        (await ordinary.ResolveLinksAsync(_principal, _context, new([last.Id, Guid.NewGuid(), first.Id]), _token))
            .Value!.Select(link => link.Id).Should().Equal(last.Id, first.Id);
        await admin.SaveCategoryAsync(_principal, _context, category.Id, new("Synthetic", ManagersOnly: true, ExpectedVersion: 1), false, _token);
        (await ordinary.ResolveLinksAsync(_principal, _context, new([first.Id]), _token)).Value.Should().BeEmpty();
        foreach (Guid[] ids in new[] { Array.Empty<Guid>(), new[] { first.Id, first.Id }, new[] { Guid.Empty }, Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray() })
        {
            (await ordinary.ResolveLinksAsync(_principal, _context, new(ids), _token)).ErrorCode.Should().Be(ResourceErrors.Invalid);
        }
        (await WorkspaceService(repository, Guid.NewGuid(), "Operator", AccessStatus.Disabled)
            .ResolveLinksAsync(_principal, _context, new([first.Id]), _token)).ErrorCode.Should().Be("AccessDenied");
    }

    private static ResourceCatalogueService WorkspaceService(IResourceRepository repository, Guid id, string role, AccessStatus status = AccessStatus.Approved)
    {
        IApplicationAccessService access = Substitute.For<IApplicationAccessService>();
        var user = new ApplicationUser(id, "synthetic", "test", status, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, 1,
            [role], AccessRoleCatalog.GetCapabilities([role]));
        access.GetCurrentAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<AccessOperationContext>(), Arg.Any<CancellationToken>())
            .Returns(AccessServiceResult<EnsureAccessUserResult>.Success(new(user, null, false, false)));
        return new(repository, access, NullLogger<ResourceCatalogueService>.Instance);
    }
}
