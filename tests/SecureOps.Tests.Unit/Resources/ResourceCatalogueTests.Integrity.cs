using System.Text.Json;
using FluentAssertions;
using SecureOps.Domain.Resources;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Resources;
using SecureOps.Shared.Contracts.Resources;

namespace SecureOps.Tests.Unit.Resources;

public sealed partial class ResourceCatalogueTests
{
    [Fact]
    public async Task SetMutation_AddRemoveReorderAndDefaultRetainRestrictedReferences()
    {
        var repository = new InMemoryResourceRepository(new InMemoryAuditWriter());
        ResourceCatalogueService admin = Service(repository, "Admin");
        ResourceCatalogueService owner = Service(repository, "Operator");
        ResourceCategory category = (await admin.SaveCategoryAsync(_principal, _context, Guid.Empty, new("Public"), true, _token)).Value!;
        ResourceCategory restricted = (await admin.SaveCategoryAsync(_principal, _context, Guid.Empty, new("Initially public"), true, _token)).Value!;
        ResourceLink first = (await admin.SaveLinkAsync(_principal, _context, Guid.Empty, Link(category.Id), true, _token)).Value!;
        ResourceLink hidden = (await admin.SaveLinkAsync(_principal, _context, Guid.Empty, Link(restricted.Id), true, _token)).Value!;
        ResourceLink last = (await admin.SaveLinkAsync(_principal, _context, Guid.Empty, Link(category.Id), true, _token)).Value!;
        ResourcePreferencesResponse personal = (await owner.SaveSetAsync(_principal, _context, Guid.Empty,
            new("Original", [first.Id, hidden.Id]), true, _token)).Value!;
        Guid setId = personal.Sets.Single().Id;
        await admin.SaveCategoryAsync(_principal, _context, restricted.Id, new("Restricted", ManagersOnly: true, ExpectedVersion: 1), false, _token);

        personal = (await owner.SaveSetAsync(_principal, _context, setId,
            new("Renamed", [last.Id, first.Id], true, personal.Version), false, _token)).Value!;
        personal.Sets.Single().Links.Select(l => l.Id).Should().Equal(last.Id, first.Id);
        personal = (await owner.SaveSetAsync(_principal, _context, setId,
            new("Renamed", [last.Id], false, personal.Version, [first.Id]), false, _token)).Value!;
        personal.DefaultSetId.Should().BeNull();
        (await owner.SaveSetAsync(_principal, _context, setId,
            new("Renamed", [], false, personal.Version, [hidden.Id]), false, _token)).ErrorCode.Should().Be(ResourceErrors.NotFound);
        (await owner.SaveSetAsync(_principal, _context, setId,
            new("Stale", [], false, personal.Version - 1), false, _token)).ErrorCode.Should().Be(ResourceErrors.Conflict);
        JsonSerializer.Serialize(personal).Should().NotContain(hidden.Id.ToString());
        await admin.SaveCategoryAsync(_principal, _context, restricted.Id, new("Restored", ExpectedVersion: 2), false, _token);
        personal = (await owner.PreferencesAsync(_principal, _context, _token)).Value!;
        personal.Sets.Single().Links.Select(l => l.Id).Should().Equal(last.Id, hidden.Id);
        personal = (await owner.SaveSetAsync(_principal, _context, setId,
            new("Empty projection is not deletion", [], false, personal.Version), false, _token)).Value!;
        personal.Sets.Single().Links.Should().HaveCount(2);
        personal = (await owner.DeleteSetAsync(_principal, _context, setId, personal.Version, _token)).Value!;
        personal.Sets.Should().BeEmpty();
    }

    [Fact]
    public async Task HiddenMembership_StillCountsTowardTheAggregateLimitWithoutBeingDisclosed()
    {
        var repository = new InMemoryResourceRepository(new InMemoryAuditWriter());
        ResourceCatalogueService admin = Service(repository, "Admin");
        ResourceCatalogueService owner = Service(repository, "Operator");
        ResourceCategory category = (await admin.SaveCategoryAsync(_principal, _context, Guid.Empty, new("Synthetic limit"), true, _token)).Value!;
        List<Guid> ids = [];
        for (int i = 0; i < 100; i++)
        {
            ids.Add((await admin.SaveLinkAsync(_principal, _context, Guid.Empty, Link(category.Id), true, _token)).Value!.Id);
        }
        ResourcePreferencesResponse saved = (await owner.SaveSetAsync(_principal, _context, Guid.Empty, new("Full", ids), true, _token)).Value!;
        Guid groupId = saved.Sets.Single().Id;
        await admin.SaveCategoryAsync(_principal, _context, category.Id, new("Restricted", ManagersOnly: true, ExpectedVersion: category.Version), false, _token);
        ResourceCategory visible = (await admin.SaveCategoryAsync(_principal, _context, Guid.Empty, new("Public"), true, _token)).Value!;
        ResourceLink addition = (await admin.SaveLinkAsync(_principal, _context, Guid.Empty, Link(visible.Id), true, _token)).Value!;
        (await owner.SaveSetAsync(_principal, _context, groupId, new("Full", [addition.Id], ExpectedVersion: saved.Version), false, _token))
            .ErrorCode.Should().Be(ResourceErrors.Limit);
        ResourcePreferencesResponse unchanged = (await owner.PreferencesAsync(_principal, _context, _token)).Value!;
        unchanged.Version.Should().Be(saved.Version);
        unchanged.Sets.Single().Links.Should().BeEmpty();
        await admin.SaveCategoryAsync(_principal, _context, category.Id, new("Restored", ExpectedVersion: category.Version + 1), false, _token);
        (await owner.ResolveSetAsync(_principal, _context, groupId, _token)).Value!.Links.Select(link => link.Id).Should().Equal(ids);
    }

    [Fact]
    public void MembershipOrdering_PreservesUnmentionedSlotsAndRejectsAmbiguousRequests()
    {
        Guid a = Guid.NewGuid(), hidden = Guid.NewGuid(), b = Guid.NewGuid(), added = Guid.NewGuid();
        ResourceSetMembership.Merge([a, hidden, b], [b, a, added], []).Should().Equal(b, hidden, a, added);
        ResourceSetMembership.Merge([a, hidden, b], [b], [a]).Should().Equal(hidden, b);
        ResourceSetMembership.Merge([a, hidden, b], [], []).Should().Equal(a, hidden, b);
        ResourceValidation.Set(new("Group", [a], RemoveLinkIds: [a])).Should().BeFalse();
        ResourceValidation.Set(new("Group", [], RemoveLinkIds: [a, a])).Should().BeFalse();
        ResourceValidation.Set(new("Group", [], RemoveLinkIds: [Guid.Empty])).Should().BeFalse();
    }

    [Fact]
    public async Task GuideDismissal_IsVersionedOwnerOnlyAndSurvivesOtherMutations()
    {
        var writer = new InMemoryAuditWriter();
        var repository = new InMemoryResourceRepository(writer);
        ResourceCatalogueService owner = Service(repository, "Operator");
        ResourceCatalogueService other = Service(repository, "Admin");
        (await owner.PreferencesAsync(_principal, _context, _token)).Value!.GuideDismissed.Should().BeFalse();
        ResourceResult<ResourcePreferencesResponse> saved = await owner.DismissGuideAsync(_principal, _context, new(0), _token);
        saved.Value!.GuideDismissed.Should().BeTrue();
        saved.Value.Version.Should().Be(1);
        (await other.PreferencesAsync(_principal, _context, _token)).Value!.GuideDismissed.Should().BeFalse();
        (await owner.DismissGuideAsync(_principal, _context, new(0), _token)).ErrorCode.Should().Be(ResourceErrors.Conflict);
        (await owner.SaveSetAsync(_principal, _context, Guid.Empty, new("New", [], ExpectedVersion: 1), true, _token)).Value!.GuideDismissed.Should().BeTrue();
        JsonSerializer.Deserialize<ResourcePreferences>("{\"Version\":1,\"FavouriteIds\":[],\"Sets\":[],\"DefaultSetId\":null}")!
            .GuideDismissed.Should().BeFalse();
    }

    [Fact]
    public async Task AnnouncementGuide_IsIndependentAuthorizedAndVersioned()
    {
        var repository = new InMemoryResourceRepository(new InMemoryAuditWriter());
        ResourceCatalogueService admin = Service(repository, "Admin"), denied = Service(repository, "Operator");
        (await denied.DismissGuideAsync(_principal, _context, new(0, "announcements"), _token)).ErrorCode.Should().Be("AccessDenied");
        (await admin.DismissGuideAsync(_principal, _context, new(0, "unknown"), _token)).ErrorCode.Should().Be(ResourceErrors.Invalid);
        ResourcePreferencesResponse saved = (await admin.DismissGuideAsync(_principal, _context, new(0, "announcements"), _token)).Value!;
        saved.AnnouncementGuideDismissed.Should().BeTrue();
        saved.GuideDismissed.Should().BeFalse();
        (await admin.DismissGuideAsync(_principal, _context, new(0, "announcements"), _token)).ErrorCode.Should().Be(ResourceErrors.Conflict);
        (await denied.PreferencesAsync(_principal, _context, _token)).Value!.AnnouncementGuideDismissed.Should().BeFalse();
        (await admin.DismissGuideAsync(_principal, _context, new(saved.Version), _token)).Value!.AnnouncementGuideDismissed.Should().BeTrue();
    }

    [Fact]
    public async Task Environments_AreBoundedSearchableAndDoNotLeakInaccessibleOrArchivedValues()
    {
        var repository = new InMemoryResourceRepository(new InMemoryAuditWriter());
        ResourceCatalogueService admin = Service(repository, "Admin");
        ResourceCatalogueService reader = Service(repository, "Operator");
        ResourceCategory category = (await admin.SaveCategoryAsync(_principal, _context, Guid.Empty, new("Public"), true, _token)).Value!;
        ResourceCategory restricted = (await admin.SaveCategoryAsync(_principal, _context, Guid.Empty, new("Restricted", ManagersOnly: true), true, _token)).Value!;
        for (int i = 0; i < 102; i++)
        {
            await admin.SaveLinkAsync(_principal, _context, Guid.Empty, Link(category.Id) with { Environment = $"Env-{i:D3}" }, true, _token);
        }
        await admin.SaveLinkAsync(_principal, _context, Guid.Empty, Link(restricted.Id) with { Environment = "Hidden" }, true, _token);
        await admin.SaveLinkAsync(_principal, _context, Guid.Empty, Link(category.Id) with { Environment = "Archived", Archived = true }, true, _token);
        ResourceEnvironmentOptions values = (await reader.EnvironmentsAsync(_principal, _context, new(), _token)).Value!;
        values.Values.Should().HaveCount(100).And.NotContain("Hidden").And.NotContain("Archived");
        values.HasMore.Should().BeTrue();
        values = (await reader.EnvironmentsAsync(_principal, _context, new(Search: "101"), _token)).Value!;
        values.Values.Should().Equal("Env-101");
        values.HasMore.Should().BeFalse();
        (await reader.EnvironmentsAsync(_principal, _context, new(IncludeArchived: true), _token)).ErrorCode.Should().Be("AccessDenied");
        (await reader.EnvironmentsAsync(_principal, _context, new(CategoryId: restricted.Id), _token)).Value!.Values.Should().BeEmpty();
        (await admin.EnvironmentsAsync(_principal, _context, new(Search: "Archived", IncludeArchived: true), _token)).Value!.Values.Should().Equal("Archived");
    }
}
