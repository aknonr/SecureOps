using FluentAssertions;
using NSubstitute;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.InUse;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Unit.InUse;

public sealed partial class InUseTests
{
    [Theory]
    [InlineData("Accept")]
    [InlineData("Reject")]
    [InlineData("Override")]
    public async Task ReporterSuggestion_ExplicitDecisionPreservesAnswersTrustedActorAndRefresh(string decision)
    {
        var mapping = new InUseReporterMappingOptions { Revision = "review-v1" };
        var f = new Fixture(mapping: mapping);
        InUseRecord imported = await f.ImportAsync();
        InUseRecord related = await InUseReporterResolverTests.RecordAsync(DateTimeOffset.UtcNow);
        InUseRecord record = imported with { Source = related.Source, Version = imported.Version + 1 };
        (await f.Repository.SaveAsync(record, imported.Version, new AuditEvent { Actor = "synthetic", Action = "SyntheticReporterEvidence" }, _token)).Should().BeTrue();
        ApplicationUser candidate = f.User with { Id = Guid.NewGuid(), DisplayName = "Reviewer different from operator", LoginName = "reviewer" };
        f.Users.GetUserAsync(candidate.Id, _token).Returns(candidate);
        mapping.Links = [new(record.Source.IdentityScope!, "00123", candidate.Id, "source-owner-review", DateTimeOffset.UtcNow.AddDays(1))];
        record = (await f.Service.SaveDraftAsync(_principal, _context, record.Id,
            new(record.Version, record.SourceVersion, [new(record.Source.Servers[0].Id, "InternetOut", "Yes", "Retained")], "Retained note"), _token)).Value!;
        InUseReporterSuggestion suggestion = (await f.Service.ReporterAsync(_principal, _context, record.Id, _token)).Value!;
        suggestion.State.Should().Be("Matched");
        (await f.Repository.GetAsync(record.Id, _token))!.AssigneeId.Should().BeNull("reading does not assign");
        var request = new AssignInUseRequest(record.Version, decision == "Accept" ? candidate.Id : decision == "Override" ? f.User.Id : null, "Reviewed suggestion")
        { ReporterDecision = decision, ReporterFingerprint = suggestion.Fingerprint };
        (await f.Service.AssignAsync(_principal, _context, record.Id, request with { ReporterFingerprint = "forged" }, _token)).Error.Should().Be("InUseConflict");
        InUseRecord assigned = (await f.Service.AssignAsync(_principal, _context, record.Id, request, _token)).Value!;
        assigned.ReporterDecision!.ActorId.Should().Be(f.User.Id);
        assigned.ReporterDecision.Proposal.Candidate!.Id.Should().Be(candidate.Id);
        assigned.ReporterDecision.Decision.Should().Be(decision);
        assigned.Draft.Should().BeEquivalentTo(record.Draft);
        assigned.AssigneeId.Should().Be(request.AssigneeId);
        (await f.Service.AssignAsync(_principal, _context, record.Id, request, _token)).Error.Should().Be("InUseConflict");
        f.Source.DiscoverAsync(_token).Returns(new InUseBatch([record.Source], false));
        (await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token)).Error.Should().BeNull();
        (await f.Repository.GetAsync(record.Id, _token))!.ReporterDecision.Should().BeEquivalentTo(assigned.ReporterDecision);
    }

    [Fact]
    public async Task ReporterSuggestion_DeniedActorCannotReadMapping()
    {
        var f = new Fixture(role: "Operator");
        (await f.Service.ReporterAsync(_principal, _context, Guid.NewGuid(), _token)).Error.Should().Be("AccessDenied");
        await f.Users.DidNotReceive().GetUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
