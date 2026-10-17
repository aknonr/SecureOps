using FluentAssertions;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.Access;

namespace SecureOps.Tests.Unit.Access;

public sealed class AccessPageFilterTests
{
    private static readonly DateTimeOffset _at = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Users_RejectedStatusIsAPendingUserWhoseLatestRequestWasRejected()
    {
        AccessUserResponse refused = User("CONTOSO\\refused", "Pending", latest: "Rejected");
        AccessUserResponse waiting = User("CONTOSO\\waiting", "Pending", latest: "Pending");

        AccessPage<AccessUserResponse> rejected = AccessPageFilter.Users([refused, waiting], new(Status: "Rejected"));
        AccessPage<AccessUserResponse> pending = AccessPageFilter.Users([refused, waiting], new(Status: "Pending"));

        rejected.Items.Should().ContainSingle().Which.CorporateIdentity.Should().Be("CONTOSO\\refused");
        pending.Items.Should().ContainSingle().Which.CorporateIdentity.Should().Be("CONTOSO\\waiting");
    }

    [Fact]
    public void Users_SearchIsCaseInsensitiveContainsAcrossNameLoginMailAndIdentity_AndRoleIsActiveRole()
    {
        AccessUserResponse ayse = User("CONTOSO\\a.yilmaz", "Approved", roles: ["Lead"], display: "Ayşe Yılmaz", mail: "ayse@example.invalid");
        AccessUserResponse other = User("CONTOSO\\other", "Approved", roles: ["ReadOnly"]);

        AccessPageFilter.Users([ayse, other], new(Search: "  AYSE@ ")).Items.Should().ContainSingle().Which.Should().BeEquivalentTo(ayse with { RequestHistory = [] });
        AccessPageFilter.Users([ayse, other], new(Search: "a.yil")).Total.Should().Be(1);
        AccessPageFilter.Users([ayse, other], new(Role: "lead")).Items.Select(user => user.CorporateIdentity).Should().Equal("CONTOSO\\a.yilmaz");
    }

    [Fact]
    public void Users_PagesAfterCountingAndDropsRequestHistoryLikeTheSqlPage()
    {
        AccessUserResponse[] users = [.. Enumerable.Range(0, 5).Select(i => User($"CONTOSO\\u{i}", "Approved"))];

        AccessPage<AccessUserResponse> page = AccessPageFilter.Users(users, new(Page: 2, PageSize: 2));

        page.Total.Should().Be(5);
        page.Items.Should().HaveCount(2);
        page.Items.Should().OnlyContain(user => user.RequestHistory.Count == 0);
        page.Items.Should().BeInAscendingOrder(user => user.UserId);
    }

    [Fact]
    public void Requests_NewestFirstAndRoleFilterUsesRequesterActiveRoles()
    {
        Guid lead = Guid.NewGuid(), plain = Guid.NewGuid();
        AccessRequestResponse older = Request(lead, "Approved", _at);
        AccessRequestResponse newer = Request(plain, "Pending", _at.AddHours(1));
        Dictionary<Guid, IReadOnlyList<string>> roles = new() { [lead] = ["Lead"], [plain] = [] };

        AccessPageFilter.Requests([older, newer], roles, new()).Items.Should().Equal(newer, older);
        AccessPageFilter.Requests([older, newer], roles, new(Role: "Lead")).Items.Should().Equal(older);
        AccessPageFilter.Requests([older, newer], roles, new(Status: "Pending")).Items.Should().Equal(newer);
    }

    [Theory]
    [InlineData(0, 25, null)]
    [InlineData(1, 101, null)]
    [InlineData(1, 25, "Unknown")]
    public void Validate_RejectsUnboundedOrUnknownQueries(int page, int size, string? status)
    {
        Action act = () => AccessPageFilter.Users([], new(Status: status, Page: page, PageSize: size));

        act.Should().Throw<ArgumentException>();
    }

    private static AccessUserResponse User(string identity, string status, string[]? roles = null, string? latest = null, string? display = null, string? mail = null)
    {
        var id = Guid.NewGuid();
        AccessRequestResponse? request = latest is null ? null : Request(id, latest, _at);
        return new(id, identity, new AccessIdentityProfileResponse(display, null, mail, null, null, null), status, roles ?? [], [], request,
            request is null ? [] : [request], 1, "test", _at, _at, null);
    }

    private static AccessRequestResponse Request(Guid user, string status, DateTimeOffset at) =>
        new(Guid.NewGuid(), user, "CONTOSO\\requester", status, at, null, null);
}
