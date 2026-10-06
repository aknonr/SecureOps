using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Integration.Api;

public sealed class InUseHostedTests
{
    [Fact]
    public async Task DirectApi_AuthorizesEveryRouteAndRejectsStaleOrInvalidDrafts()
    {
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            foreach ((string key, string value) in new Dictionary<string, string>
            {
                ["DemoAuth:Enabled"] = "true",
                ["Access:DemoCompatibilityEnabled"] = "true",
                ["Audit:Provider"] = "InMemory",
                ["IdentityLookup:Provider"] = "Mock",
                ["OperationalRecords:SourceProvider"] = "fake",
                ["Jira:Provider"] = "Fake",
                // Existing Fake/Test profile is distinct from corporate read-only mode; no real adapter is selected.
                ["OperationalRecords:ReadOnlyIntegrationMode"] = "false",
                ["OperationalRecords:ControlledTestWritesEnabled"] = "false",
                ["OperationalRecords:SourceCloseEnabled"] = "false",
                ["RateLimiting:OperationalRecordRefresh:PermitLimit"] = "100"
            })
            { builder.UseSetting(key, value); }
        });
        using HttpClient admin = factory.CreateApiClient();
        admin.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", "platform-admin");
        using HttpClient denied = factory.CreateApiClient();
        denied.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", "team-lead");
        (await admin.GetFromJsonAsync<InUsePage>("/api/v1/in-use"))!.Total.Should().Be(0);
        var command = new RefreshInUseRequest(Guid.NewGuid());
        (await admin.PostAsJsonAsync("/api/v1/in-use/refresh", command)).EnsureSuccessStatusCode();
        InUsePage page = (await admin.GetFromJsonAsync<InUsePage>("/api/v1/in-use?pageSize=1"))!;
        page.Total.Should().Be(2);
        page.Items.Should().ContainSingle();
        (await admin.GetFromJsonAsync<InUsePage>("/api/v1/in-use?sort=newest&view=verification"))!.Items
            .Should().OnlyContain(r => r.ActivityStatus == "VerificationPending");
        (await admin.GetFromJsonAsync<InUsePage>("/api/v1/in-use?sort=oldest&view=pending"))!.Total.Should().Be(0,
            "local source without activity evidence must not invent eligible WASAS activities");
        InUseRecord record = page.Items[0];
        string root = $"/api/v1/in-use/{record.Id}";
        (await denied.PostAsJsonAsync(root + "/execution/manual-verification", new ConfirmInUseClosureRequest(Guid.NewGuid(), 1, record.Source.Code)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.GetAsync("/api/v1/in-use/reports")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.GetAsync(root + "/reporter-suggestion")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.PostAsJsonAsync(root + "/reports/index", new IndexInUseReportsRequest(record.Version, [1]))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        foreach (string invalid in new[] { "page=0", "pageSize=101", "status=Attached", "version=0", "from=2026-09-20&to=2026-09-19" })
        { (await admin.GetAsync("/api/v1/in-use/reports?" + invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest); }
        // In-memory mode must not manufacture a successful empty SQL catalogue.
        (await admin.GetAsync("/api/v1/in-use/reports")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await admin.PostAsJsonAsync(root + "/reports/index", new IndexInUseReportsRequest(record.Version, []))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        InUseReporterSuggestion suggestion = (await admin.GetFromJsonAsync<InUseReporterSuggestion>(root + "/reporter-suggestion"))!;
        suggestion.Candidate.Should().BeNull();
        (await admin.GetFromJsonAsync<InUseRecord>(root))!.Version.Should().Be(record.Version);
        record.AssigneeId.Should().BeNull();
        (await denied.GetAsync("/api/v1/in-use/overview")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetFromJsonAsync<InUseOverview>("/api/v1/in-use/overview"))!.Unknown.Should().Be(2);
        var intent = new ConfirmInUseRequest(record.Version, Guid.NewGuid(), new string('A', 64));
        (await denied.PostAsJsonAsync(root + "/completion-intent", intent)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsJsonAsync(root + "/completion-intent", intent)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        record = (await (await admin.PutAsJsonAsync(root + "/draft", new SaveInUseDraftRequest(record.Version, record.SourceVersion, [], "")))
            .Content.ReadFromJsonAsync<InUseRecord>())!;
        record.AssigneeId.Should().BeNull();
        (await admin.PostAsJsonAsync(root + "/report", new ExportInUseRequest(record.Version))).EnsureSuccessStatusCode();
        (await denied.PostAsJsonAsync(root + "/relationship-evidence", new InUseDiagnosticRequest(record.Version, record.Source.Id))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsJsonAsync(root + "/relationship-evidence", new InUseDiagnosticRequest(record.Version, "other"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await denied.GetAsync("/api/v1/in-use")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.GetAsync(root)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.GetAsync("/api/v1/in-use/assignees")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.PostAsJsonAsync("/api/v1/in-use/refresh", command)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.PutAsJsonAsync(root + "/assignment", new AssignInUseRequest(record.Version, null, "Denied"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.PutAsJsonAsync(root + "/draft", new SaveInUseDraftRequest(record.Version, 1, [], ""))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.PostAsJsonAsync(root + "/report", new ExportInUseRequest(record.Version))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.PostAsJsonAsync(root + "/report", new ExportInUseRequest(record.Version, true))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.PostAsJsonAsync(root + "/report", new ExportInUseRequest(record.Version, ArchivedVersion: 1))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        foreach (string query in new[] { "page=0", "pageSize=101", "view=other", "status=closed", "sort=other" })
        { (await admin.GetAsync("/api/v1/in-use?" + query)).StatusCode.Should().Be(HttpStatusCode.BadRequest); }
        JsonElement me = await admin.GetFromJsonAsync<JsonElement>("/api/v1/access/me");
        record = (await (await admin.PutAsJsonAsync(root + "/assignment", new AssignInUseRequest(record.Version,
            me.GetProperty("userId").GetGuid(), "Synthetic manual assignment"))).Content.ReadFromJsonAsync<InUseRecord>())!;
        (await admin.GetFromJsonAsync<InUsePage>("/api/v1/in-use?view=mine"))!.Total.Should().Be(1);
        (await admin.GetFromJsonAsync<InUsePage>("/api/v1/in-use?view=unassigned"))!.Total.Should().Be(1);
        foreach (InUseAnswer[] answers in new[] {
            new[] { new InUseAnswer("missing", "InternetOut", "Yes", "evidence") },
            new[] { new InUseAnswer(record.Source.Servers[0].Id, "InternetOut", "Assumed", "evidence") } })
        { (await admin.PutAsJsonAsync(root + "/draft", new SaveInUseDraftRequest(record.Version, 1, answers, ""))).StatusCode.Should().Be(HttpStatusCode.BadRequest); }
        InUseRecord saved = (await (await admin.PutAsJsonAsync(root + "/draft", new SaveInUseDraftRequest(record.Version, 1, [], "Unknown checks retained")))
            .Content.ReadFromJsonAsync<InUseRecord>())!;
        (await admin.PostAsJsonAsync(root + "/report", new ExportInUseRequest(record.Version))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsJsonAsync(root + "/report", new ExportInUseRequest(saved.Version))).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync(root + "/report", new ExportInUseRequest(saved.Version, true))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // Replaying the same refresh command cannot read source again or invalidate a saved draft.
        (await admin.PostAsJsonAsync("/api/v1/in-use/refresh", command)).EnsureSuccessStatusCode();
        (await admin.GetFromJsonAsync<InUseRecord>(root))!.Version.Should().Be(saved.Version);
        JsonElement records = await admin.GetFromJsonAsync<JsonElement>("/api/v1/operational-records/stored");
        records.GetProperty("total").GetInt32().Should().Be(0);
        var discard = new ChangeInUseDraftRequest(saved.Version, "Discard", "Synthetic trial removed");
        (await denied.PostAsJsonAsync(root + "/draft-lifecycle", discard)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        InUseRecord removed = (await (await admin.PostAsJsonAsync(root + "/draft-lifecycle", discard)).Content.ReadFromJsonAsync<InUseRecord>())!;
        removed.Discarded.Should().BeTrue();
        removed.Draft.Should().BeNull();
        (await admin.PostAsJsonAsync(root + "/draft-lifecycle", discard)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.GetFromJsonAsync<InUsePage>("/api/v1/in-use?status=Discarded"))!.Items.Should().ContainSingle(r => r.Id == removed.Id);
        (await admin.GetFromJsonAsync<InUsePage>("/api/v1/in-use"))!.Items.Should().NotContain(r => r.Id == removed.Id);
        InUseRecord restarted = (await (await admin.PostAsJsonAsync(root + "/draft-lifecycle", new ChangeInUseDraftRequest(removed.Version, "Restart", "Fresh review"))).Content.ReadFromJsonAsync<InUseRecord>())!;
        restarted.Discarded.Should().BeFalse();
        restarted.Draft.Should().BeNull();
        restarted.AssigneeId.Should().Be(saved.AssigneeId);
    }
}
