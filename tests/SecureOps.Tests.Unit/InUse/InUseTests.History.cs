using FluentAssertions;
using NSubstitute;
using SecureOps.Infrastructure.InUse;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Unit.InUse;

public sealed partial class InUseTests
{
    [Fact]
    public async Task ReviewHistory_CopySnapshotSurvivesSourceAnswerEdit_AndReuseRequiresExplicitContextMatch()
    {
        var f = new Fixture();
        InUseRecord record = await f.ImportAsync();
        InUseServer[] servers = record.Source.Servers.Select(s => s with
        {
            Fields = s.Fields.Concat(new Dictionary<string, InUseEvidence>
            {
                ["NETWORK SEGMENT"] = new("synthetic-network", "fixture"),
                ["ITMC_Service_ID"] = new("0002915", "fixture")
            }).ToDictionary(p => p.Key, p => p.Value)
        }).ToArray();
        InUseSource source = record.Source with { Servers = servers };
        f.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(new InUseBatch([source], false));
        await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token);
        record = (await f.Repository.GetAsync(record.Id, _token))!;
        InUseAnswer[] answers = servers.SelectMany((s, i) => InUseChecks.OperatorCodes.Select(c => new InUseAnswer(s.Id, c, i == 0 ? "No" : "Yes", "")
        { Origin = i == 0 ? new("Individual") : new("Bulk", servers[0].Id) { CopiedValue = "Yes" } })).ToArray();
        record = (await f.Service.SaveDraftAsync(_principal, _context, record.Id, new(record.Version, record.SourceVersion, answers, ""), _token)).Value!;
        record.Draft!.Answers.Where(a => a.ServerId == servers[1].Id).Should().OnlyContain(a => a.Origin!.Kind == "Bulk"
            && a.Origin.AcceptedBy == f.User.Id && a.Origin.CopiedValue == "Yes");
        InUseServerHistory history = (await f.Service.HistoryAsync(_principal, _context, record.Id, servers[1].Id, null, 1, 10, _token)).Value!;
        history.Total.Should().Be(1);
        history.Items[0].Answers.Should().BeEquivalentTo(record.Draft.Answers.Where(a => a.ServerId == servers[1].Id));
        InUseSource next = source with { Id = "900099", Code = "OR-SECOND-REVIEW" };
        f.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(new InUseBatch([source, next], false));
        await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token);
        InUseRecord second = (await f.Repository.QueryAsync(new(Search: next.Code), f.User.Id, _token)).Items.Single();
        second.Draft.Should().BeNull();
        history = (await f.Service.HistoryAsync(_principal, _context, second.Id, servers[1].Id, null, 1, 10, _token)).Value!;
        history.Proposals.Single().CanReuse.Should().BeTrue();
        InUseAnswer reused = new(servers[1].Id, "InternetOut", "Yes", "")
        { Origin = new("PreviousReview", ReviewId: history.Items[0].Id, AcceptedBy: Guid.NewGuid()) };
        InUseResult<InUseRecord> saved = await f.Service.SaveDraftAsync(_principal, _context, second.Id,
            new(second.Version, second.SourceVersion, [reused], ""), _token);
        saved.Error.Should().BeNull();
        saved.Value!.Draft!.Answers.Single().Origin!.AcceptedBy.Should().Be(f.User.Id);
        saved.Value.Draft.Answers.Single().Origin!.ReviewId.Should().Be(history.Items[0].Id);
        InUseServer changed = servers[1] with
        {
            Fields = servers[1].Fields.ToDictionary(p => p.Key,
            p => p.Key == "IP ADDRESS" ? new InUseEvidence("192.0.2.99", "changed") : p.Value)
        };
        InUseReviewHistory.Proposal(second with { Source = next with { Servers = [changed] } }, changed, history.Items[0], DateTimeOffset.UtcNow)
            .CanReuse.Should().BeFalse();
        InUseReviewHistory.Proposal(second with { LastSeenAt = DateTimeOffset.UtcNow.AddDays(-2) }, servers[1], history.Items[0], DateTimeOffset.UtcNow)
            .CanReuse.Should().BeFalse();
        InUseReviewHistory.Identity(next with { IdentityScope = null }, servers[1]).Should().BeNull();
        InUseReviewHistory.Identity(next with { IdentityScope = "simulation:another-tenant" }, servers[1]).Should().NotBe(history.IdentityKey);
    }

    [Fact]
    public async Task ReviewHistory_RejectsFabricatedCopies_AndDoesNotConvertUnknownIntoNo()
    {
        var f = new Fixture();
        InUseRecord record = await f.ImportAsync();
        InUseAnswer answer = new(record.Source.Servers[0].Id, "InternetOut", "Unknown", "");
        InUseRecord saved = (await f.Service.SaveDraftAsync(_principal, _context, record.Id,
            new(record.Version, record.SourceVersion, [answer], ""), _token)).Value!;
        saved.Draft!.Answers.Single().Value.Should().Be("Unknown");
        InUseAnswer invalid = answer with { Value = "Yes", Origin = new("Bulk", "not-this-or") { CopiedValue = "Yes" } };
        (await f.Service.SaveDraftAsync(_principal, _context, record.Id,
            new(saved.Version, saved.SourceVersion, [invalid], ""), _token)).Error.Should().Be("InUseConflict");
        (await f.Repository.GetAsync(record.Id, _token))!.Draft.Should().BeEquivalentTo(saved.Draft);
    }
}
