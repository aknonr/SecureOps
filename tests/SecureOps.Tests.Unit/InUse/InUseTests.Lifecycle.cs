using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.InUse;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Unit.InUse;

public sealed partial class InUseTests
{
    [Fact]
    public async Task DraftRecovery_PreservesArchive_RefreshCannotResurrectTrialAnswers()
    {
        var archive = new InUseReportArchive(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["InUseReports:Directory"] = Path.Combine(Path.GetTempPath(), "inuse-recovery-" + Guid.NewGuid()) }).Build());
        var f = new Fixture(archive: archive);
        InUseRecord record = await f.ImportAsync();
        InUseAnswer[] answers = record.Source.Servers.SelectMany(s => InUseChecks.OperatorCodes.Select(c => new InUseAnswer(s.Id, c, "No", ""))).ToArray();
        record = (await f.Service.SaveDraftAsync(_principal, _context, record.Id, new(record.Version, record.SourceVersion, answers, "Trial"), _token)).Value!;
        InUseReport original = (await f.Service.ExportAsync(_principal, _context, record.Id, new(record.Version, true), _token)).Value!;
        long oldVersion = record.Version;
        record = (await f.Service.ChangeDraftAsync(_principal, _context, record.Id, new(record.Version, "Reset", "Trial answers"), _token)).Value!;
        record.Draft.Should().BeNull();
        record.InvalidatedReviewsThrough.Should().Be(oldVersion);
        record.DraftLifecycle!.ActorId.Should().Be(f.User.Id);
        (await f.Service.SaveDraftAsync(_principal, _context, record.Id, new(oldVersion, record.SourceVersion, answers, "Stale save"), _token)).Error.Should().Be("InUseConflict");
        record = (await f.Service.ChangeDraftAsync(_principal, _context, record.Id, new(record.Version, "Discard", "Trial removed"), _token)).Value!;
        (await f.Repository.QueryAsync(new(), f.User.Id, _token)).Items.Should().NotContain(r => r.Id == record.Id);
        (await f.Repository.QueryAsync(new(Status: "Discarded"), f.User.Id, _token)).Items.Should().Contain(r => r.Id == record.Id);
        (await f.Service.SaveDraftAsync(_principal, _context, record.Id, new(record.Version, record.SourceVersion, answers, ""), _token)).Error.Should().Be("InUseConflict");
        await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token);
        record = (await f.Service.GetAsync(_principal, _context, record.Id, _token)).Value!;
        record.Discarded.Should().BeTrue();
        record.Draft.Should().BeNull();
        InUseReport historical = (await f.Service.ExportAsync(_principal, _context, record.Id, new(record.Version, ArchivedVersion: original.Version), _token)).Value!;
        historical.Content.Should().Equal(original.Content);
        historical.Sha256.Should().Be(original.Sha256);
        record = (await f.Service.ChangeDraftAsync(_principal, _context, record.Id, new(record.Version, "Restart", "Fresh review"), _token)).Value!;
        record.Discarded.Should().BeFalse();
        record.Draft.Should().BeNull();
        record.ArchivedVersions.Should().Contain(original.Version);
        foreach (InUseServer server in record.Source.Servers)
        {
            InUseServerHistory history = (await f.Service.HistoryAsync(_principal, _context, record.Id, server.Id, null, 1, 50, _token)).Value!;
            history.Items.Should().NotBeEmpty().And.OnlyContain(h => h.Invalidated);
            history.Proposals.Should().OnlyContain(p => !p.CanReuse);
        }
        record = (await f.Service.SaveDraftAsync(_principal, _context, record.Id, new(record.Version, record.SourceVersion, answers, "Reviewed again"), _token)).Value!;
        InUseReport next = (await f.Service.ExportAsync(_principal, _context, record.Id, new(record.Version, true), _token)).Value!;
        next.Version.Should().BeGreaterThan(original.Version);
        (await f.Service.ExportAsync(_principal, _context, record.Id, new(record.Version, ArchivedVersion: original.Version), _token)).Value!.Content.Should().Equal(original.Content);
    }

    [Theory]
    [InlineData("Delete")]
    [InlineData("Restart")]
    public async Task DraftRecovery_RejectsInvalidTransitions(string action)
    {
        var f = new Fixture();
        InUseRecord record = await f.ImportAsync();
        InUseResult<InUseRecord> result = await f.Service.ChangeDraftAsync(_principal, _context, record.Id, new(record.Version, action, "Reason"), _token);
        result.Error.Should().NotBeNull();
        (await f.Repository.GetAsync(record.Id, _token))!.Version.Should().Be(record.Version);
    }
}
