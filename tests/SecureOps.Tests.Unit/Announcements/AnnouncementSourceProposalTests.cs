using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Announcements.Sources;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Announcements;
using Xunit;

namespace SecureOps.Tests.Unit.Announcements;

public sealed class AnnouncementSourceProposalTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    [InlineData("Maintenance {WorkStart} to {WorkEnd}; restart reviewed separately.")]
    public void Proposal_UsesValidatedDescriptionAndNeverDerivesRestartOrServicesFromScope(string template)
    {
        var profile = new MaintenanceProfileOptions
        {
            CollectionId = "SYNTHETIC",
            Scope = "Reviewed scope",
            Impact = "Impact",
            Checks = "Checks",
            Description = "Reviewed literal description",
            DescriptionTemplate = template
        };
        var options = new AnnouncementSourceOptions();
        options.Profiles["NonProd"] = profile;
        var catalog = new MaintenanceProfileCatalog(Options.Create(options));
        catalog.Resolve("NonProd").State.Should().Be("Configured");
        Guid owner = Guid.NewGuid(), id = Guid.NewGuid(), jobId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-09-21T00:00:00Z");
        var content = new AnnouncementContent("OCO-TEST", "Manual scope", "Subject", "", "", "", "Manual description",
            "", "", "", [], [], "synthetic", RestartStart: "2026-09-21T03:15:00+03:00");
        var draft = new AnnouncementDraft(id, owner, 1, now, content, "synthetic", "hash");
        var snapshot = new AnnouncementSourceSnapshot(jobId, id, owner, "NonProd", "OCO-TEST", now, "SYNTHETIC",
            [new("device-1", "SYNTHETIC", now)], [new("Actual service", ["device-1"], "Resolved", now)],
            new("2026-09-21T01:00:00+03:00", "2026-09-21T05:00:00+03:00", "2026-09-21", "Unresolved", "Unresolved", now),
            new(true, 1, 1, 1, 1, 0, 0, 0, false, []));
        var job = new AnnouncementSourceJob(jobId, owner, id, "NonProd", "OCO-TEST", "Succeeded", now, now, null, snapshot);
        // Exercise the pure formatter without creating SQL stores, a host or a provider.
        var service = new AnnouncementSourceService(null!, null!, null!, catalog, null!, null!,
            Options.Create(options), Options.Create(new AnnouncementOptions()), TimeProvider.System,
            NullLogger<AnnouncementSourceService>.Instance);
        MethodInfo build = typeof(AnnouncementSourceService).GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var proposal = (AnnouncementSourceProposal)build.Invoke(service,
            [job, snapshot, draft, profile, AnnouncementSourceOverrides.Empty(id, owner), null])!;
        string? description = proposal.Fields.Single(field => field.Field == "Description").Proposed;
        if (string.IsNullOrWhiteSpace(template))
        { description.Should().Be(profile.Description); }
        else
        { description.Should().Contain("05:00").And.NotContain("{WorkEnd}").And.NotContain("03:15"); }
        proposal.Fields.Single(field => field.Field == "WorkEnd").Proposed.Should().Be(snapshot.Work!.ProposedFinishText);
        proposal.Fields.Where(field => field.Field.StartsWith("Restart", StringComparison.Ordinal))
            .Should().OnlyContain(field => field.Proposed == null && field.Origin == "NotDerivable" && field.State == "SourceUnavailable");
        content.RestartStart.Should().Be("2026-09-21T03:15:00+03:00");
        proposal.ProposedAffectedServices.Should().Equal("Actual service");
    }
}
