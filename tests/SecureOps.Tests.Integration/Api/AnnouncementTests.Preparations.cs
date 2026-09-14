using System.Net;
using System.Net.Http.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using MimeKit;
using NSubstitute;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Announcements;
using SecureOps.Shared.Contracts.Announcements;

namespace SecureOps.Tests.Integration.Api;

public sealed class PreparationSqlFactAttribute : FactAttribute
{
    public PreparationSqlFactAttribute() { if (Environment.GetEnvironmentVariable("SECUREOPS_PREPARATION_SQL") != "1") { Skip = "Requires fresh preparation-only local harness."; } }
}
public sealed partial class AnnouncementTests
{
    [Fact]
    public void Preparation_LegacyFingerprintSurvivesOptionalSourceReviewMetadata()
    {
        var legacy = new
        {
            Id = Guid.NewGuid(),
            Draft = new AnnouncementDraft(Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UnixEpoch, FinalContent(), "sender@example.invalid", "hash"),
            PreparedAt = DateTimeOffset.UnixEpoch,
            PreparedBy = "Synthetic",
            Fingerprint = "",
            Html = "<p>Local</p>",
            Email = new byte[] { 1, 2, 3 },
            AssetRevisions = new Dictionary<string, string>(),
            ArtifactType = "FinalAnnouncement",
            State = "Prepared"
        };
        string original = System.Text.Json.JsonSerializer.Serialize(legacy);
        string fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(original)));
        System.Text.Json.Nodes.JsonNode document = System.Text.Json.Nodes.JsonNode.Parse(original)!;
        document["Fingerprint"] = fingerprint;
        PreparedAnnouncement restored = System.Text.Json.JsonSerializer.Deserialize<PreparedAnnouncement>(document.ToJsonString())!;
        restored.SourceReview.Should().BeNull();
        AnnouncementService.PreparationFingerprint(restored).Should().Be(fingerprint);
    }

    [PreparationSqlFact]
    public async Task Preparation_ExactPersistedBytesOwnerPagingIdempotencyAndAuditRollback()
    {
        string connection = Environment.GetEnvironmentVariable("SECUREOPS_PREPARATION_SQL_CONNECTION")!;
        var guard = new SqlConnectionStringBuilder(connection);
        guard.DataSource.Should().Be("(localdb)\\SecureOpsResourcesV1");
        guard.InitialCatalog.Should().StartWith("SecureOps_ResourcesV1_OcoPreparation");
        guard.IntegratedSecurity.Should().BeTrue();
        string assets = Assets();
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Test");
            foreach ((string k, string v) in new Dictionary<string, string>
            {
                ["DemoAuth:Enabled"] = "true",
                ["Access:DemoCompatibilityEnabled"] = "true",
                ["Access:RepositoryProvider"] = "SqlServer",
                ["SessionSecurity:RepositoryProvider"] = "SqlServer",
                ["ConnectionStrings:SecureOpsDb"] = connection,
                ["Audit:Provider"] = "InMemory",
                ["IdentityLookup:Provider"] = "Mock",
                ["OperationalRecords:SourceProvider"] = "Disabled",
                ["Jira:Provider"] = "Disabled",
                ["OperationalRecords:ReadOnlyIntegrationMode"] = "false",
                ["OperationalRecords:ControlledTestWritesEnabled"] = "false",
                ["OperationalRecords:SourceCloseEnabled"] = "false",
                ["Announcements:Enabled"] = "true",
                ["Announcements:Sender"] = "announcements@example.invalid",
                ["Announcements:AssetDirectory"] = assets,
                ["Announcements:Banners:synthetic-v1"] = "banner.bin",
                ["Announcements:Bundles:bundle-v1:Footer"] = "Yerel test altbilgisi"
            })
            { b.UseSetting(k, v); }
            foreach (string role in _roles)
            { b.UseSetting("Announcements:Bundles:bundle-v1:Assets:" + role, "synthetic-v1"); }
        });
        using HttpClient admin = factory.CreateClient(), denied = factory.CreateClient(), anonymous = factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", "platform-admin");
        denied.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", "team-lead");
        await using var sql = new SqlConnection(connection);
        string draft = "/api/v1/announcements/" + Guid.NewGuid(), root = "/api/v1/announcements/preparations";
        (await admin.PutAsJsonAsync(draft, FinalContent())).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/v1/announcements/preview", FinalContent())).EnsureSuccessStatusCode();
        (await admin.GetAsync(draft + "?version=1&format=eml")).EnsureSuccessStatusCode();
        int downloads = await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditLog WHERE Action='AnnouncementDownloadPrepared'");
        downloads.Should().Be(1);
        (await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM announcements.Preparations")).Should().Be(0);
        string incomplete = "/api/v1/announcements/" + Guid.NewGuid();
        (await admin.PutAsJsonAsync(incomplete, FinalContent() with { Subject = "" })).EnsureSuccessStatusCode();
        using HttpResponseMessage incompleteResponse = await admin.PutAsync(root + "/" + Guid.NewGuid() + "?draftId=" + incomplete.Split('/')[^1] + "&version=1", null);
        incompleteResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await incompleteResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("fields").EnumerateArray().Select(f => f.GetString()).Should().Contain("Subject");
        Guid id = Guid.NewGuid(), draftId = Guid.Parse(draft.Split('/')[^1]);
        string path = root + "/" + id, command = path + $"?draftId={draftId}&version=1";
        (await anonymous.GetAsync(root)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await denied.PutAsync(command, null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        HttpResponseMessage[] concurrent = await Task.WhenAll(admin.PutAsync(command, null), admin.PutAsync(command, null));
        foreach (HttpResponseMessage r in concurrent)
        { r.EnsureSuccessStatusCode(); }
        PreparedAnnouncement first = (await concurrent[0].Content.ReadFromJsonAsync<PreparedAnnouncement>())!;
        (await concurrent[1].Content.ReadFromJsonAsync<PreparedAnnouncement>())!.Fingerprint.Should().Be(first.Fingerprint);
        first.State.Should().Be("Prepared");
        (await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditLog WHERE Action='AnnouncementDownloadPrepared'")).Should().Be(downloads);
        first.ArtifactType.Should().Be("FinalAnnouncement");
        first.AssetRevisions.Keys.Should().BeEquivalentTo(_roles);
        first.Fingerprint.Should().Be(AnnouncementService.PreparationFingerprint(first));
        byte[]? captured = null;
        IAnnouncementDispatchClaim captureClaim = Substitute.For<IAnnouncementDispatchClaim>();
        IAnnouncementTransport captureTransport = Substitute.For<IAnnouncementTransport>();
        captureClaim.ClaimAsync(id, first.Fingerprint, default).Returns(true);
        captureTransport.SubmitAsync(first, default).Returns(_ => { captured = first.Email.ToArray(); return new AnnouncementTransportOutcome("LocalCapture", [], []); });
        (await new AnnouncementDispatchBoundary(captureClaim, captureTransport).ExecuteAsync(first, default)).State.Should().Be("LocalCapture");
        captured.Should().Equal(first.Email);
        using var mail = MimeMessage.Load(new MemoryStream(first.Email));
        foreach (string service in Services())
        { mail.TextBody.Should().Contain(service); mail.HtmlBody.Should().Contain(WebUtility.HtmlEncode(service)); }
        foreach (MimePart image in mail.BodyParts.OfType<MimePart>().Where(p => p.ContentId is not null))
        { using var bytes = new MemoryStream(); image.Content!.DecodeTo(bytes); first.Html.Should().Contain(Convert.ToBase64String(bytes.ToArray())); }
        mail.BodyParts.OfType<MimePart>().Count(p => p.ContentId is not null).Should().Be(6);
        (await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM announcements.Preparations")).Should().Be(1);
        (await admin.PutAsJsonAsync(draft + "?version=1", FinalContent() with { Subject = "Edited later" })).EnsureSuccessStatusCode();
        (await admin.PutAsync(command, null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PutAsync(path + $"?draftId={draftId}&version=2", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        string second = root + "/" + Guid.NewGuid();
        (await admin.PutAsync(second + $"?draftId={draftId}&version=2", null)).EnsureSuccessStatusCode();
        foreach (string invalid in new[] { "page=0", "pageSize=101" })
        { (await admin.GetAsync(root + "?" + invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest); }
        PreparationPage page = (await admin.GetFromJsonAsync<PreparationPage>(root + "?pageSize=1"))!;
        page.Total.Should().Be(2);
        page.Items.Single().Subject.Should().Be("Edited later");
        (await admin.GetFromJsonAsync<PreparationPage>(root + "?page=2&pageSize=1"))!.Items.Single().Id.Should().Be(id);
        (await admin.GetFromJsonAsync<PreparationPage>(root + "?page=3&pageSize=1"))!.Items.Should().BeEmpty();
        var store = new SqlAnnouncementStore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:SecureOpsDb"] = connection }).Build());
        (await store.PreparedAsync(id, Guid.NewGuid(), default)).Should().BeNull();
        (await store.PrepareAsync(first with { Html = "different content" }, "synthetic", default)).Error.Should().Be("AnnouncementPreparationConflict");
        (await store.PreparationPageAsync(Guid.NewGuid(), 1, 25, "synthetic", default)).Total.Should().Be(0);
        string trigger = "TR_PreparationTest_" + Guid.NewGuid().ToString("N");
        await sql.ExecuteAsync($"CREATE TRIGGER audit.[{trigger}] ON audit.AuditLog AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Action='AnnouncementPrepared') THROW 51179,'Synthetic failure.',1; END;");
        try
        { (await admin.PutAsync(root + "/" + Guid.NewGuid() + $"?draftId={draftId}&version=2", null)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable); }
        finally { await sql.ExecuteAsync($"DROP TRIGGER audit.[{trigger}];"); }
        (await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM announcements.Preparations")).Should().Be(2);
        await FluentActions.Awaiting(() => sql.ExecuteAsync("UPDATE announcements.Preparations SET Subject='changed';")).Should().ThrowAsync<SqlException>();
        File.Move(Path.Combine(assets, "banner.bin"), Path.Combine(assets, "retained.bin"));
        PreparedAnnouncement historical = (await admin.GetFromJsonAsync<PreparedAnnouncement>(path))!;
        historical.Email.Should().Equal(first.Email);
        historical.Html.Should().Be(first.Html);
        historical.Draft.Version.Should().Be(1);
        (await denied.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PutAsync(root + "/" + Guid.NewGuid() + $"?draftId={draftId}&version=2", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
    [Theory]
    [InlineData("LocalCapture")]
    [InlineData("KnownPreSubmissionFailure")]
    [InlineData("PartialRecipientAcceptance")]
    [InlineData("UnknownOutcome")]
    public async Task DispatchBoundary_LocalContractHasNoRetryOrSendingRegistration(string state)
    {
        var snapshot = new PreparedAnnouncement(Guid.NewGuid(), new(Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UtcNow, Content(), "sender@example.invalid", "hash"), DateTimeOffset.UtcNow, "Synthetic", "", "inert", [1, 2, 3], new Dictionary<string, string>());
        snapshot = snapshot with { Fingerprint = AnnouncementService.PreparationFingerprint(snapshot) };
        IAnnouncementDispatchClaim claim = Substitute.For<IAnnouncementDispatchClaim>();
        IAnnouncementTransport transport = Substitute.For<IAnnouncementTransport>();
        var boundary = new AnnouncementDispatchBoundary(claim, transport);
        (await boundary.ExecuteAsync(snapshot, default)).State.Should().Be("NotDispatched");
        claim.ClaimAsync(snapshot.Id, snapshot.Fingerprint, default).Returns(true, false);
        transport.SubmitAsync(snapshot, default).Returns(new AnnouncementTransportOutcome(state, state == "PartialRecipientAcceptance" ? ["reader@example.invalid"] : [], []));
        if (state == "UnknownOutcome")
        { transport.SubmitAsync(snapshot, default).Returns<Task<AnnouncementTransportOutcome>>(_ => throw new IOException("Uncertain local test submission")); }
        (await boundary.ExecuteAsync(snapshot, default)).State.Should().Be(state);
        (await boundary.ExecuteAsync(snapshot, default)).State.Should().Be("NotDispatched");
        await transport.Received(1).SubmitAsync(snapshot, default);
        (await boundary.ExecuteAsync(snapshot with { Html = "tampered" }, default)).State.Should().Be("NotDispatched");
    }
}
