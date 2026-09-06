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

public sealed class ResourceCatalogueTests
{
    private static readonly ClaimsPrincipal _principal = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "synthetic")], "test"));
    private static readonly AccessOperationContext _context = new("synthetic", "resource-test", null);
    private static readonly CancellationToken _token = CancellationToken.None;

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("ResourceCurator", true)]
    [InlineData("Lead", false)]
    [InlineData("Operator", false)]
    [InlineData("Auditor", false)]
    [InlineData("JiraPublisher", false)]
    [InlineData("ReadOnly", false)]
    public async Task Management_RequiresExplicitCapability(string role, bool allowed)
    {
        var repository = new InMemoryResourceRepository(new InMemoryAuditWriter());
        ResourceCatalogueService service = Service(repository, role);
        ResourceResult<ResourceCategory> result = await service.SaveCategoryAsync(_principal, _context, Guid.Empty, new("Synthetic"), true, _token);
        result.IsSuccess.Should().Be(allowed);
        (await repository.CategoriesAsync(true, true, _token)).Count.Should().Be(allowed ? 1 : 0);
        AccessRoleCatalog.GetCapabilities([role]).Should().Contain(Capabilities.ResourcesView);
        if (!allowed)
        {
            result.ErrorCode.Should().Be("AccessDenied");
        }
    }

    [Theory]
    [InlineData(AccessStatus.Pending)]
    [InlineData(AccessStatus.Disabled)]
    public async Task NonApprovedUser_IsDeniedEvenWithManagerCapabilities(AccessStatus status)
    {
        IResourceRepository repository = Substitute.For<IResourceRepository>();
        ResourceCatalogueService service = Service(repository, "Admin", status: status);
        (await service.QueryAsync(_principal, _context, new(), _token)).ErrorCode.Should().Be("AccessDenied");
        (await service.PreferencesAsync(_principal, _context, _token)).ErrorCode.Should().Be("AccessDenied");
        await repository.DidNotReceiveWithAnyArgs().QueryAsync(default!, default, default);
        await repository.DidNotReceiveWithAnyArgs().PreferencesAsync(default, default);
    }

    [Theory]
    [InlineData("https://example.invalid/dashboard?orgId=1&from=now-6h&to=now", true)]
    [InlineData("https://example.invalid/dashboard?view=summary&theme=dark", true)]
    [InlineData("http://example.invalid", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///c:/synthetic", false)]
    [InlineData("data:text/html,synthetic", false)]
    [InlineData("https://user:synthetic@example.invalid/", false)]
    [InlineData("https://example.invalid/?token=synthetic", false)]
    [InlineData("https://example.invalid/?password=synthetic", false)]
    [InlineData("https://example.invalid/?access_token=synthetic", false)]
    [InlineData("https://example.invalid/?redirect=https://elsewhere.invalid", false)]
    [InlineData("https://example.invalid/?%74ab=x", false)]
    [InlineData("https://example.invalid/?tab=%2574oken", false)]
    [InlineData("https://example.invalid/?tab=x&tab=y", false)]
    [InlineData("https://example.invalid/#token=synthetic", false)]
    [InlineData("https://example.invalid/%0d%0a", false)]
    [InlineData("https://example.invalid/path;session=synthetic", false)]
    [InlineData("https://example.invalid/\\evil", false)]
    [InlineData("not-a-url", false)]
    public void UrlPolicy_IsPositiveAndNeverFetches(string url, bool valid) => ResourceValidation.Url(url).Should().Be(valid);

    [Theory]
    [InlineData("<script>")]
    [InlineData("text\nline")]
    [InlineData("text\u202Ehidden")]
    [InlineData("")]
    public void RequiredContent_RejectsMarkupControlsAndEmpty(string value) => ResourceValidation.Text(value, 80, true).Should().BeFalse();

    [Fact]
    public void Bounds_RejectOverlongFieldsTagsQueriesAndDuplicates()
    {
        SaveResourceLinkRequest request = Link(Guid.NewGuid());
        ResourceValidation.Link(request with { Notes = new string('x', 1001) }).Should().BeFalse();
        ResourceValidation.Link(request with { Tags = ["same", "SAME"] }).Should().BeFalse();
        ResourceValidation.Link(request with { Tags = [null!] }).Should().BeFalse();
        ResourceValidation.Link(request with { Name = new string('x', 121) }).Should().BeFalse();
        ResourceValidation.Query(new(Page: 0)).Should().BeFalse();
        ResourceValidation.Query(new(PageSize: 101)).Should().BeFalse();
        ResourceValidation.Query(new(Search: new string('x', 101))).Should().BeFalse();
        var id = Guid.NewGuid();
        ResourceValidation.Set(new("Synthetic", [id, id])).Should().BeFalse();
    }

    [Fact]
    public async Task Search_FiltersVisibilityAndStablePagination()
    {
        var repository = new InMemoryResourceRepository(new InMemoryAuditWriter());
        ResourceCatalogueService admin = Service(repository, "Admin");
        ResourceCategory category = (await admin.SaveCategoryAsync(_principal, _context, Guid.Empty, new("Synthetic"), true, _token)).Value!;
        ResourceCategory hidden = (await admin.SaveCategoryAsync(_principal, _context, Guid.Empty, new("Restricted", ManagersOnly: true), true, _token)).Value!;
        ResourceLink first = (await admin.SaveLinkAsync(_principal, _context, Guid.Empty, Link(category.Id) with { DisplayOrder = 1 }, true, _token)).Value!;
        ResourceLink second = (await admin.SaveLinkAsync(_principal, _context, Guid.Empty, Link(category.Id) with { DisplayOrder = 2 }, true, _token)).Value!;
        ResourceLink restricted = (await admin.SaveLinkAsync(_principal, _context, Guid.Empty, Link(hidden.Id), true, _token)).Value!;
        ResourceCatalogueService reader = Service(repository, "Operator");
        ResourceQuery query = new(Search: "synthetic", CategoryId: category.Id, Environment: "test", Location: "lab", Tag: "sample", PageSize: 1);
        ResourcePage page = (await reader.QueryAsync(_principal, _context, query, _token)).Value!;
        page.Total.Should().Be(2);
        page.Items.Select(l => l.Id).Should().Equal(first.Id);
        (await reader.QueryAsync(_principal, _context, query with { Page = 2 }, _token)).Value!.Items.Select(l => l.Id).Should().Equal(second.Id);
        (await reader.QueryAsync(_principal, _context, query with { Page = 3 }, _token)).Value!.Items.Should().BeEmpty();
        (await reader.QueryAsync(_principal, _context, query with { Search = "dashboard Synthetic" }, _token)).Value!.Total.Should().Be(0);
        (await reader.GetAsync(_principal, _context, restricted.Id, false, _token)).ErrorCode.Should().Be(ResourceErrors.NotFound);
        (await reader.QueryAsync(_principal, _context, new(IncludeArchived: true), _token)).ErrorCode.Should().Be("AccessDenied");
    }

    [Fact]
    public async Task OwnedSets_PreserveOrderDefaultAndHideArchivedReferences()
    {
        var writer = new InMemoryAuditWriter();
        var repository = new InMemoryResourceRepository(writer);
        ResourceCatalogueService admin = Service(repository, "Admin");
        ResourceCategory category = (await admin.SaveCategoryAsync(_principal, _context, Guid.Empty, new("Synthetic"), true, _token)).Value!;
        ResourceLink first = (await admin.SaveLinkAsync(_principal, _context, Guid.Empty, Link(category.Id), true, _token)).Value!;
        ResourceLink second = (await admin.SaveLinkAsync(_principal, _context, Guid.Empty, Link(category.Id), true, _token)).Value!;
        ResourceCatalogueService owner = Service(repository, "Operator");
        ResourceCatalogueService other = Service(repository, "Operator");
        ResourcePreferencesResponse personal = (await owner.SaveSetAsync(_principal, _context, Guid.Empty, new("Synthetic start", [second.Id, first.Id], true), true, _token)).Value!;
        Guid setId = personal.Sets.Single().Id;
        personal.Sets.Single().Links.Select(l => l.Id).Should().Equal(second.Id, first.Id);
        personal.DefaultSetId.Should().Be(setId);
        (await other.ResolveSetAsync(_principal, _context, setId, _token)).ErrorCode.Should().Be(ResourceErrors.NotFound);
        (await other.SaveSetAsync(_principal, _context, setId, new("Attempt", []), false, _token)).ErrorCode.Should().Be(ResourceErrors.NotFound);
        (await other.DeleteSetAsync(_principal, _context, setId, 0, _token)).ErrorCode.Should().Be(ResourceErrors.NotFound);
        personal = (await owner.FavouriteAsync(_principal, _context, first.Id, new(true, personal.Version), _token)).Value!;
        (await other.PreferencesAsync(_principal, _context, _token)).Value!.Favourites.Should().BeEmpty();
        await admin.SaveLinkAsync(_principal, _context, first.Id, Link(category.Id) with { Archived = true, ExpectedVersion = first.Version }, false, _token);
        personal = (await owner.PreferencesAsync(_principal, _context, _token)).Value!;
        personal.Favourites.Should().BeEmpty();
        personal.Sets.Single().Links.Select(l => l.Id).Should().Equal(second.Id);
        await admin.SaveCategoryAsync(_principal, _context, category.Id, new("Synthetic", ManagersOnly: true, ExpectedVersion: category.Version), false, _token);
        (await owner.ResolveSetAsync(_principal, _context, setId, _token)).Value!.Links.Should().BeEmpty();
        personal = (await owner.DeleteSetAsync(_principal, _context, setId, personal.Version, _token)).Value!;
        personal.DefaultSetId.Should().BeNull();
        personal.Sets.Should().BeEmpty();
    }

    [Fact]
    public async Task Concurrency_RejectsStaleSharedAndPersonalUpdates()
    {
        var repository = new InMemoryResourceRepository(new InMemoryAuditWriter());
        ResourceCatalogueService admin = Service(repository, "Admin");
        ResourceCategory category = (await admin.SaveCategoryAsync(_principal, _context, Guid.Empty, new("Synthetic"), true, _token)).Value!;
        ResourceResult<ResourceCategory>[] changes = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            admin.SaveCategoryAsync(_principal, _context, category.Id, new("Edited", ExpectedVersion: 1), false, _token)));
        changes.Count(r => r.IsSuccess).Should().Be(1);
        changes.Count(r => r.ErrorCode == ResourceErrors.Conflict).Should().Be(9);
        await admin.SaveSetAsync(_principal, _context, Guid.Empty, new("One", [], true), true, _token);
        (await admin.SaveSetAsync(_principal, _context, Guid.Empty, new("Stale", [], true), true, _token)).ErrorCode.Should().Be(ResourceErrors.Conflict);
        ResourcePreferencesResponse current = (await admin.SaveSetAsync(_principal, _context, Guid.Empty, new("Two", [], true, 1), true, _token)).Value!;
        current.Sets.Count(s => s.IsDefault).Should().Be(1);
        (await admin.SaveSetAsync(_principal, _context, Guid.Empty, new("two", [], false, 2), true, _token)).ErrorCode.Should().Be(ResourceErrors.Invalid);
    }

    [Fact]
    public async Task AuditFailure_PreventsMutationAndAuditContainsNoLinkContent()
    {
        IAuditWriter writer = Substitute.For<IAuditWriter>();
        var repository = new InMemoryResourceRepository(writer);
        ResourceCatalogueService admin = Service(repository, "Admin");
        ResourceCategory category = (await admin.SaveCategoryAsync(_principal, _context, Guid.Empty, new("Sensitive synthetic name"), true, _token)).Value!;
        var audit = (AuditEvent)writer.ReceivedCalls().Single().GetArguments()[0]!;
        JsonSerializer.Serialize(audit).Should().NotContain("Sensitive synthetic name");
        writer.WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException("synthetic failure")));
        (await admin.SaveCategoryAsync(_principal, _context, category.Id, new("Modified", ExpectedVersion: 1), false, _token)).ErrorCode.Should().Be("PersistenceUnavailable");
        (await repository.CategoriesAsync(true, true, _token)).Single().Version.Should().Be(1);
    }

    internal static SaveResourceLinkRequest Link(Guid category) => new(category, "Synthetic dashboard", "https://example.invalid/dashboard?orgId=1",
        "Synthetic purpose", Environment: "Test", Location: "Lab", Tags: ["sample"]);

    [Fact]
    public async Task FutureExpectedVersion_CannotOverwriteStateBuiltFromAnOlderSnapshot()
    {
        IResourceRepository repository = Substitute.For<IResourceRepository>();
        repository.PreferencesAsync(Arg.Any<Guid>(), _token).Returns(ResourcePreferences.Empty);
        repository.ResolveAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<bool>(), _token).Returns([]);
        ResourceCatalogueService service = Service(repository, "Operator");
        (await service.SaveSetAsync(_principal, _context, Guid.Empty, new("Synthetic", [], ExpectedVersion: 1), true, _token))
            .ErrorCode.Should().Be(ResourceErrors.Conflict);
        await repository.DidNotReceiveWithAnyArgs().SavePreferencesAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task PersonalLimits_DefaultAndEmptySetsRemainBounded()
    {
        var repository = new InMemoryResourceRepository(new InMemoryAuditWriter());
        ResourceCatalogueService service = Service(repository, "Operator");
        for (int index = 0; index < 20; index++)
        {
            (await service.SaveSetAsync(_principal, _context, Guid.Empty, new("Synthetic " + index, [], ExpectedVersion: index), true, _token)).IsSuccess.Should().BeTrue();
        }
        (await service.SaveSetAsync(_principal, _context, Guid.Empty, new("Over limit", [], ExpectedVersion: 20), true, _token)).ErrorCode.Should().Be(ResourceErrors.Limit);
        ResourcePreferencesResponse current = (await service.PreferencesAsync(_principal, _context, _token)).Value!;
        current.Version.Should().Be(20);
        current.Sets.Should().HaveCount(20);
        current.DefaultSetId.Should().BeNull();
        current.Sets.Should().OnlyContain(s => s.Links.Count == 0);
    }

    private static ResourceCatalogueService Service(IResourceRepository repository, string role, AccessStatus status = AccessStatus.Approved)
    {
        IApplicationAccessService access = Substitute.For<IApplicationAccessService>();
        var user = new ApplicationUser(Guid.NewGuid(), "synthetic", "test", status, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, 1, [role], AccessRoleCatalog.GetCapabilities([role]));
        access.GetCurrentAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<AccessOperationContext>(), Arg.Any<CancellationToken>())
            .Returns(AccessServiceResult<EnsureAccessUserResult>.Success(new(user, null, false, false)));
        return new(repository, access, NullLogger<ResourceCatalogueService>.Instance);
    }
}
