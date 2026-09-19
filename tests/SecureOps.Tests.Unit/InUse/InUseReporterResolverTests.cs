using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.InUse;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Unit.InUse;

public sealed class InUseReporterResolverTests
{
    [Theory]
    [InlineData("matched", "Matched")]
    [InlineData("disabled", "Ineligible")]
    [InlineData("capability", "Ineligible")]
    [InlineData("missing-user", "NoMatch")]
    [InlineData("ambiguous", "Ambiguous")]
    [InlineData("other-scope", "NoMatch")]
    [InlineData("expired", "MappingUnverified")]
    [InlineData("unconfigured", "MappingNotConfigured")]
    [InlineData("stale", "ReporterUnverified")]
    [InlineData("wrong-parent", "ReporterUnverified")]
    public async Task Resolver_UsesExactReviewedIdentity_NeverNameSimilarity(string mode, string expected)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var user = new ApplicationUser(Guid.NewGuid(), "synthetic:stable-user", "test", AccessStatus.Approved,
            now, now, null, 1, [], [Capabilities.InUseView, Capabilities.InUseReview], DisplayName: "Different display name");
        IAccessRepository users = Substitute.For<IAccessRepository>();
        users.GetUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(mode switch
        {
            "disabled" => user with { Status = AccessStatus.Disabled },
            "capability" => user with { Capabilities = [Capabilities.InUseView] },
            "missing-user" => null,
            _ => user
        });
        InUseRecord record = await RecordAsync(now);
        if (mode is "stale" or "wrong-parent")
        { record = record with { Source = record.Source with { Servers = record.Source.Servers.Select(s => s with { RelatedRequestReporter = s.RelatedRequestReporter! with { LastVerifiedAt = mode == "stale" ? now.AddDays(-2) : now, ParentId = mode == "wrong-parent" ? "other-parent" : record.Source.Id } }).ToArray() } }; }
        var link = new InUseReporterIdentityLink(mode == "other-scope" ? "other:tenant" : record.Source.IdentityScope!,
            "00123", user.Id, "source-owner-review-1", mode == "expired" ? now : now.AddDays(1));
        var options = new InUseReporterMappingOptions { Revision = mode == "unconfigured" ? "" : "review-v1", Links = mode == "ambiguous" ? [link, link] : [link] };
        var resolver = new InUseReporterResolver(Options.Create(options), users);
        InUseReporterSuggestion proposal = await resolver.ResolveAsync(record, now, default);
        proposal.State.Should().Be(expected);
        if (expected == "Matched")
        {
            proposal.Candidate!.Id.Should().Be(user.Id);
            proposal.Candidate.Label.Should().StartWith("Different display name");
            proposal.Fingerprint.Should().HaveLength(64);
            InUseReporterSuggestion changed = await resolver.ResolveAsync(record with { Version = record.Version + 1 }, now, default);
            changed.Fingerprint.Should().NotBe(proposal.Fingerprint);
        }
        else
        { proposal.Candidate.Should().BeNull(); }
        await users.DidNotReceive().ListUsersAsync(Arg.Any<CancellationToken>());
    }

    internal static async Task<InUseRecord> RecordAsync(DateTimeOffset now)
    {
        InUseSource source = (await new LocalInUseSourceClient().DiscoverAsync(default)).Records[0];
        source = source with
        {
            Servers = source.Servers.Select(s => s with
            {
                RelatedRequestReporter = new(source.Id, s.Id, "OR-001", "Code", "111", "OR-001",
                    "Source business reporter", "00123", "ExactMatch", "Returned", "Returned", now)
            }).ToArray()
        };
        return new(Guid.NewGuid(), source, "source-hash", 3, 10, null, null, null, now);
    }
}
