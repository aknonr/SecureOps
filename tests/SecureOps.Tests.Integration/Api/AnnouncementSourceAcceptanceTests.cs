using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hangfire;
using Hangfire.SqlServer;
using MimeKit;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Announcements.Sources;
using SecureOps.Shared.Contracts.Announcements;
using SecureOps.Tests.Integration.Sql;
using Xunit.Abstractions;

namespace SecureOps.Tests.Integration.Api;

[Collection("Announcement SQL")]
public sealed class AnnouncementSourceAcceptanceTests(ITestOutputHelper output)
{
    [SourceHostFact]
    public async Task ApiAndWorker_ExecuteReviewedJourneyAndRecoverAnActualProcessInterruption()
    {
        var watch = Stopwatch.StartNew();
        await using var hosts = new AnnouncementSourceHosts();
        output.WriteLine("Evidence: " + hosts.Root);
        await hosts.FixturesAsync();
        Process api = hosts.Start("SecureOps.Api");
        using HttpClient admin = hosts.Client(), lead = hosts.Client("team-lead"), anonymous = hosts.Client(null);
        await AnnouncementSourceHosts.UntilAsync(async () =>
        {
            if (api.HasExited)
            { throw new InvalidOperationException("Local API exited; inspect retained host log."); }
            try
            { return (await admin.GetAsync("/api/v1/access/me")).IsSuccessStatusCode; }
            catch (HttpRequestException) { return false; }
        }, TimeSpan.FromSeconds(40));
        Guid owner = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/access/me")).GetProperty("userId").GetGuid();
        Guid other = (await lead.GetFromJsonAsync<JsonElement>("/api/v1/access/me")).GetProperty("userId").GetGuid();
        var id = Guid.NewGuid();
        string path = "/api/v1/announcements/" + id;
        AnnouncementContent content = SourceTestDatabase.Content() with
        { TemplateRevision = "oco-table-v2", BannerRevision = "synthetic-bundle", AffectedServices = ["Manual service"] };
        (await admin.PutAsJsonAsync(path, content)).EnsureSuccessStatusCode();
        foreach (HttpClient denied in new[] { anonymous, lead })
        {
            HttpStatusCode expected = denied == anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            (await denied.GetAsync("/api/v1/announcements/source/profiles")).StatusCode.Should().Be(expected);
            (await denied.PostAsJsonAsync(path + "/source/jobs", new AnnouncementSourceSubmission("NonProd", "OCO-TEST", "denied-key"))).StatusCode.Should().Be(expected);
            (await denied.GetAsync(path + "/source/jobs")).StatusCode.Should().Be(expected);
            (await denied.GetAsync(path + "/source/jobs/" + Guid.NewGuid() + "/proposal")).StatusCode.Should().Be(expected);
            (await denied.PostAsJsonAsync(path + "/source/apply", new AnnouncementSourceApply(Guid.NewGuid(), 1, [], false, false))).StatusCode.Should().Be(expected);
        }
        AnnouncementSourceJobStatus first = await SubmitAsync(admin, path, "NonProd", "first-key");
        AnnouncementSourceJobStatus duplicate = await SubmitAsync(admin, path, "NonProd", "first-key", " OCO-TEST ");
        duplicate.DuplicateOf.Should().Be(first.JobId);
        (await admin.PostAsJsonAsync(path + "/source/jobs", new AnnouncementSourceSubmission("Prod01", "OCO-TEST", "first-key"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsJsonAsync(path + "/source/jobs", new AnnouncementSourceSubmission("NonProd", "OCO-OTHER", "first-key"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsJsonAsync(path + "/source/jobs", new { profile = "NonProd", ocoReference = "OCO-TEST", submissionKey = "unknown-key", ownerId = other })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        hosts.Start("SecureOps.Worker");
        await CompletedAsync(admin, path, first.JobId);
        (await hosts.Database.Drafts.GetAsync(id, owner, default))!.Version.Should().Be(1);
        AnnouncementSourceProposal proposal = await ProposalAsync(admin, path, first.JobId);
        string readFailure = await hosts.Database.FailureAsync("audit.AuditLog", $"Actor='{owner}' AND Action='AnnouncementSourceRead'");
        try
        {
            foreach (string read in new[] { "/api/v1/announcements/source/profiles", path + "/source/jobs", path + "/source/jobs/" + first.JobId + "/proposal" })
            {
                using HttpResponseMessage failed = await admin.GetAsync(read);
                failed.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
                (await failed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("AnnouncementSourceUnavailable");
            }
        }
        finally { await hosts.Database.ExecuteAsync("DROP TRIGGER " + readFailure); }
        (await admin.GetAsync("/api/v1/announcements/source/profiles")).EnsureSuccessStatusCode();
        proposal.ProposedAffectedServices.Should().Equal("Service A", "Service B");
        proposal.Audience.Should().Be("DistributionRequest");
        proposal.Fields.Single(f => f.Field == "WorkStart").SourceText.Should().Be("2026-09-15T01:00:00.123+03:00");
        proposal.Fields.Where(f => f.Field.StartsWith("Restart", StringComparison.Ordinal)).Should().OnlyContain(f => f.Proposed == null);
        await ApplyAsync(admin, path, proposal, true);
        AnnouncementContent applied = (await admin.GetFromJsonAsync<AnnouncementContent>(path))!;
        applied.WorkStart.Should().Be(content.WorkStart);
        applied.Scope.Should().Be("NonProd scope");
        applied.To.Should().Contain("manual@example.invalid").And.Contain("remove@example.invalid");
        applied.Cc.Should().NotIntersectWith(applied.To);
        applied.AffectedServices.Should().Equal("Service A", "Service B");
        string preparedPath = "/api/v1/announcements/preparations/" + Guid.NewGuid();
        using HttpResponseMessage preparedResponse = await admin.PutAsync(preparedPath + "?draftId=" + id + "&version=2", null);
        preparedResponse.EnsureSuccessStatusCode();
        PreparedAnnouncement prepared = (await preparedResponse.Content.ReadFromJsonAsync<PreparedAnnouncement>())!;
        prepared.SourceReview!.AppliedJobId.Should().Be(first.JobId);
        prepared.SourceReview.Profile.Should().Be("NonProd");
        string html = await admin.GetStringAsync(path + "?version=2&format=html");
        byte[] eml = await admin.GetByteArrayAsync(path + "?version=2&format=eml");
        await File.WriteAllTextAsync(Path.Combine(hosts.Root, "reviewed-v2.html"), html);
        await File.WriteAllBytesAsync(Path.Combine(hosts.Root, "reviewed-v2.eml"), eml);
        using MimeMessage message = await MimeMessage.LoadAsync(new MemoryStream(eml));
        message.To.Mailboxes.Select(m => m.Address).Should().BeEquivalentTo(applied.To);
        html.Should().Contain("Service A").And.Contain("Service B");
        message.Subject.Should().Be(content.Subject);
        (await admin.PostAsJsonAsync(path + "/source/apply", new AnnouncementSourceApply(first.JobId, 1, [], false, false))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsJsonAsync(path + "/source/apply", new AnnouncementSourceApply(first.JobId, 2, ["Unexpected"], false, false, 1))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The second authorized owner must still receive misses, never another owner's evidence.
        await hosts.Database.ExecuteAsync("INSERT INTO security.RoleAssignments(UserId,RoleId,GrantedByCorporateIdentity) VALUES(@other,1,'synthetic-source-test')", new { other });
        (await lead.GetAsync("/api/v1/announcements/source/profiles")).EnsureSuccessStatusCode();
        (await lead.GetAsync(path + "/source/jobs?jobId=" + first.JobId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await lead.GetAsync(path + "/source/jobs/" + first.JobId + "/proposal")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await lead.PostAsJsonAsync(path + "/source/apply", new AnnouncementSourceApply(first.JobId, 2, [], false, false, 1))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await lead.PostAsJsonAsync(path + "/source/jobs", new AnnouncementSourceSubmission("NonProd", "OCO-TEST", "other-key"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        string otherPath = "/api/v1/announcements/" + Guid.NewGuid();
        (await lead.PutAsJsonAsync(otherPath, content)).EnsureSuccessStatusCode();

        applied = applied with { To = ["nonprod@example.invalid", "manual@example.invalid", "added@example.invalid"] };
        (await admin.PutAsJsonAsync(path + "?version=2", applied)).EnsureSuccessStatusCode();
        AnnouncementSourceOverrides before = await hosts.Database.Sources.OverridesAsync(id, owner, default);
        AnnouncementSourceJobStatus second = await SubmitAsync(admin, path, "Prod01", "decline-recipients");
        await CompletedAsync(admin, path, second.JobId);
        await ApplyAsync(admin, path, await ProposalAsync(admin, path, second.JobId), false);
        AnnouncementSourceOverrides declined = await hosts.Database.Sources.OverridesAsync(id, owner, default);
        declined.Profile.Should().Be(before.Profile);
        declined.ManualTo.Should().Equal(before.ManualTo);
        declined.ManualCc.Should().Equal(before.ManualCc);
        declined.RemovedTo.Should().Equal(before.RemovedTo);
        declined.RemovedCc.Should().Equal(before.RemovedCc);
        (await admin.GetFromJsonAsync<AnnouncementContent>(path))!.To.Should().Equal(applied.To);
        AnnouncementSourceJobStatus third = await SubmitAsync(admin, path, "Prod01", "accept-new-profile");
        await CompletedAsync(admin, path, third.JobId);
        AnnouncementSourceProposal switched = await ProposalAsync(admin, path, third.JobId);
        switched.To.Proposed.Should().Contain("added@example.invalid").And.Contain("manual@example.invalid")
            .And.NotContain("nonprod@example.invalid").And.NotContain("remove@example.invalid");
        (await admin.PostAsJsonAsync(path + "/source/apply", new AnnouncementSourceApply(third.JobId, 4, ["Scope"], true, false, 0))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await ApplyAsync(admin, path, switched, true);
        (await admin.PostAsJsonAsync(path + "/source/apply", new AnnouncementSourceApply(first.JobId, 5, ["Scope"], true, false, 3))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await lead.GetFromJsonAsync<AnnouncementContent>(otherPath)).Should().BeEquivalentTo(content);

        await hosts.FixturesAsync(delay: 30000);
        AnnouncementSourceJobStatus interrupted = await SubmitAsync(admin, path, "Prod02", "interrupted-worker");
        await AnnouncementSourceHosts.UntilAsync(async () => (await hosts.Database.Sources.GetAsync(interrupted.JobId, owner, default))!.State == "Running", TimeSpan.FromSeconds(20));
        Guid obsolete = await hosts.Database.ScalarAsync<Guid>("SELECT AttemptId FROM announcements.SourceJobs WHERE JobId=@id", new { id = interrupted.JobId });
        await hosts.KillWorkerAsync();
        await hosts.FixturesAsync();
        hosts.Start("SecureOps.Worker");
        // Real SQL lease expiry and Hangfire's minute scheduler recover the killed process. No clock/row reset.
        await CompletedAsync(admin, path, interrupted.JobId, TimeSpan.FromSeconds(190));
        (await hosts.Database.ScalarAsync<int>("SELECT AttemptCount FROM announcements.SourceJobs WHERE JobId=@id", new { id = interrupted.JobId })).Should().Be(2);
        (await hosts.Database.Sources.CompleteAsync(interrupted.JobId, obsolete, "Failed", "Obsolete", null, "late", DateTimeOffset.UtcNow, default)).Should().BeFalse();
        (await hosts.Database.Drafts.GetAsync(id, owner, default))!.Version.Should().Be(5);
        PreparedAnnouncement retained = (await admin.GetFromJsonAsync<PreparedAnnouncement>(preparedPath))!;
        retained.Email.Should().Equal(prepared.Email);
        retained.SourceReview.Should().BeEquivalentTo(prepared.SourceReview);
        retained.Fingerprint.Should().Be(prepared.Fingerprint);
        var storage = new SqlServerStorage(hosts.Database.Connection, new SqlServerStorageOptions { PrepareSchemaIfNecessary = false });
        new HangfireAnnouncementSourceDispatcher(new BackgroundJobClient(storage), hosts.Queue).Enqueue(interrupted.JobId);
        await AnnouncementSourceHosts.UntilAsync(async () => await hosts.Database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM HangFire.Job WHERE StateName='Succeeded' AND Arguments LIKE @pattern",
            new { pattern = "%" + interrupted.JobId + "%" }) >= 2, TimeSpan.FromSeconds(20));
        (await hosts.Database.ScalarAsync<int>("SELECT AttemptCount FROM announcements.SourceJobs WHERE JobId=@id", new { id = interrupted.JobId })).Should().Be(2);
        (await hosts.Database.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditLog WHERE Action LIKE '%Sent%' AND Actor=@actor", new { actor = owner.ToString("D") })).Should().Be(0);
        await File.WriteAllTextAsync(Path.Combine(hosts.Root, "acceptance.json"), JsonSerializer.Serialize(new
        { ApiPid = api.Id, WorkerPid = hosts.Worker!.Id, hosts.Address, hosts.Queue, DraftId = id, FirstJob = first.JobId, RecoveredJob = interrupted.JobId, Attempts = 2, DraftVersion = 5, ElapsedSeconds = watch.Elapsed.TotalSeconds }));
        output.WriteLine("API/Worker acceptance passed in {0:F1}s; queue={1}; recoveredJob={2}", watch.Elapsed.TotalSeconds, hosts.Queue, interrupted.JobId);
    }

    private static async Task<AnnouncementSourceJobStatus> SubmitAsync(HttpClient client, string path, string profile, string key, string oco = "OCO-TEST")
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path + "/source/jobs", new AnnouncementSourceSubmission(profile, oco, key));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AnnouncementSourceJobStatus>())!;
    }
    private static Task<AnnouncementSourceProposal> ProposalAsync(HttpClient client, string path, Guid jobId) =>
        client.GetFromJsonAsync<AnnouncementSourceProposal>(path + "/source/jobs/" + jobId + "/proposal")!;
    private static async Task ApplyAsync(HttpClient client, string path, AnnouncementSourceProposal proposal, bool recipients)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path + "/source/apply", new AnnouncementSourceApply(
            proposal.JobId, proposal.DraftVersion, ["Scope"], recipients, true, proposal.OverrideVersion));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
    private static async Task CompletedAsync(HttpClient client, string path, Guid id, TimeSpan? timeout = null)
    {
        AnnouncementSourceJobStatus? status = null;
        await AnnouncementSourceHosts.UntilAsync(async () =>
        {
            status = await client.GetFromJsonAsync<AnnouncementSourceJobStatus>(path + "/source/jobs?jobId=" + id);
            return status!.Terminal;
        }, timeout ?? TimeSpan.FromSeconds(25));
        status!.State.Should().Be("Succeeded", status.ErrorCode);
    }
}

public sealed class SourceHostFactAttribute : FactAttribute
{
    public SourceHostFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SECUREOPS_SOURCE_HOST_ACCEPTANCE") != "1")
        { Skip = "Explicit isolated API/Worker process acceptance opt-in required."; }
    }
}
