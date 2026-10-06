using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MimeKit;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Announcements;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Announcements;
using SecureOps.Tests.Integration.Sql;
using SkiaSharp;
using Xunit.Abstractions;

namespace SecureOps.Tests.Integration.Api;

[Collection("Announcement SQL")]
public sealed partial class AnnouncementTests(ITestOutputHelper output)
{
    private static AnnouncementContent Content() => new("OCO-SYNTHETIC", "Manual scope", "Planlı & çalışma",
        "2026-09-12", "2026-09-13T01:00+03:00", "2026-09-13T02:00+03:00", "Türkçe &lt;b&gt; <script>alert(1)</script>",
        "Kısa kesinti", "Kontroller", "", ["reader@example.invalid", "READER@example.invalid"], ["reader@example.invalid", "copy@example.invalid"], "synthetic-v1");
    private static string Assets(SKEncodedImageFormat type = SKEncodedImageFormat.Png)
    {
        string root = Path.Combine(Path.GetTempPath(), "wasas-oco-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var bitmap = new SKBitmap(64, 24);
        bitmap.Erase(SKColors.Crimson);
        using SKData data = bitmap.Encode(type, 90);
        File.WriteAllBytes(Path.Combine(root, "banner.bin"), data.ToArray());
        return root;
    }
    [Theory]
    [InlineData("x@example.invalid\r\nBcc:bad@example.invalid")]
    [InlineData("Name <x@example.invalid>")]
    [InlineData("x@example.invalid,y@example.invalid")]
    public void Recipients_RejectHeaderInjectionAndLists(string address) => AnnouncementValidation.Address(address).Should().BeFalse();

    [Fact]
    public void Validation_PreservesRawTextAndSeparatesDates()
    {
        AnnouncementValidation.Errors(Content(), true).Should().BeEmpty();
        AnnouncementValidation.Errors(Content() with { Subject = "x\r\nInjected: y", WorkStart = "2026-09-13T01:00", To = [] }, true)
            .Should().Contain(["Subject", "WorkStart", "To"]);
        AnnouncementValidation.Errors(Content() with { WorkEnd = "2026-09-12T01:00Z", RestartStart = "2026-09-12T01:00Z" }, true).Should().Contain(["WorkEnd", "RestartEnd"]);
    }
    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    public async Task Assets_ValidateActualCodecAndRejectCorruption(SKEncodedImageFormat type)
    {
        string root = Assets(type);
        var renderer = new AnnouncementRenderer(Options.Create(new AnnouncementOptions { AssetDirectory = root, Banners = new() { ["synthetic-v1"] = "banner.bin" } }));
        (await renderer.AssetAsync("synthetic-v1", default)).Type.Should().Be(type == SKEncodedImageFormat.Png ? "png" : "jpeg");
        await File.WriteAllBytesAsync(Path.Combine(root, "banner.bin"), new byte[64]);
        await FluentActions.Awaiting(() => renderer.AssetAsync("synthetic-v1", default)).Should().ThrowAsync<InvalidOperationException>();
        using var wide = new SKBitmap(2049, 1);
        using SKData encoded = wide.Encode(SKEncodedImageFormat.Png, 90);
        await File.WriteAllBytesAsync(Path.Combine(root, "banner.bin"), encoded.ToArray());
        await FluentActions.Awaiting(() => renderer.AssetAsync("synthetic-v1", default)).Should().ThrowAsync<InvalidOperationException>();
        await File.WriteAllBytesAsync(Path.Combine(root, "banner.bin"), new byte[262145]);
        await FluentActions.Awaiting(() => renderer.AssetAsync("synthetic-v1", default)).Should().ThrowAsync<InvalidOperationException>();
    }

    [LocalResourceSqlFact]
    public async Task DraftApi_PersistsOwnRevisionsPreviewsAndDownloadsWithoutSending()
    {
        string connection = Environment.GetEnvironmentVariable("SECUREOPS_SQL_TEST_CONNECTION")!;
        var guard = new SqlConnectionStringBuilder(connection);
        guard.DataSource.Should().BeEquivalentTo("(localdb)\\SecureOpsResourcesV1");
        guard.InitialCatalog.Should().StartWith("SecureOps_ResourcesV1_Oco");
        guard.IntegratedSecurity.Should().BeTrue();
        string assets = Assets();
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Test");
            foreach ((string key, string value) in new Dictionary<string, string>
            {
                ["DemoAuth:Enabled"] = "true",
                ["Access:DemoCompatibilityEnabled"] = "true",
                ["Access:RepositoryProvider"] = "SqlServer",
                ["SessionSecurity:RepositoryProvider"] = "SqlServer",
                ["ConnectionStrings:SecureOpsDb"] = connection,
                ["IdentityLookup:Provider"] = "Mock",
                ["Audit:Provider"] = "InMemory",
                ["OperationalRecords:SourceProvider"] = "Disabled",
                ["Jira:Provider"] = "Disabled",
                ["OperationalRecords:ReadOnlyIntegrationMode"] = "false",
                ["OperationalRecords:ControlledTestWritesEnabled"] = "false",
                ["OperationalRecords:SourceCloseEnabled"] = "false",
                ["Announcements:Enabled"] = "true",
                ["Announcements:Sender"] = "announcements@example.invalid",
                ["Announcements:AssetDirectory"] = assets,
                ["Announcements:Banners:synthetic-v1"] = "banner.bin",
                ["Announcements:Bundles:bundle-v1:Footer"] = "Yerel sentetik altbilgi",
                ["Announcements:Bundles:bundle-v1:Assets:header"] = "synthetic-v1",
                ["Announcements:Bundles:bundle-v1:Assets:main"] = "synthetic-v1",
                ["Announcements:Bundles:bundle-v1:Assets:logo"] = "synthetic-v1",
                ["Announcements:Bundles:bundle-v1:Assets:linkedin"] = "synthetic-v1",
                ["Announcements:Bundles:bundle-v1:Assets:instagram"] = "synthetic-v1",
                ["Announcements:Bundles:bundle-v1:Assets:youtube"] = "synthetic-v1"
            })
            { b.UseSetting(key, value); }
        });
        using HttpClient admin = factory.CreateApiClient(), denied = factory.CreateApiClient(), anonymous = factory.CreateApiClient();
        admin.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", "platform-admin");
        denied.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", "team-lead");
        await SeedSenderAsync(admin, connection);
        Shared.Contracts.Resources.ResourcePreferencesResponse? preferences = await admin.GetFromJsonAsync<SecureOps.Shared.Contracts.Resources.ResourcePreferencesResponse>("/api/v1/resources/me");
        (await admin.PutAsJsonAsync("/api/v1/resources/me/guide", new { expectedVersion = preferences!.Version, guide = "announcements" })).EnsureSuccessStatusCode();
        Shared.Contracts.Resources.ResourcePreferencesResponse? persistedPreferences = await admin.GetFromJsonAsync<SecureOps.Shared.Contracts.Resources.ResourcePreferencesResponse>("/api/v1/resources/me");
        persistedPreferences!.AnnouncementGuideDismissed.Should().BeTrue();
        persistedPreferences.GuideDismissed.Should().Be(preferences.GuideDismissed);
        (await denied.PutAsJsonAsync("/api/v1/resources/me/guide", new { expectedVersion = 0, guide = "announcements" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var id = Guid.NewGuid();
        string path = "/api/v1/announcements/" + id;
        (await anonymous.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await denied.PutAsJsonAsync(path, Content())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using HttpResponseMessage saved = await admin.PutAsJsonAsync(path, Content());
        saved.EnsureSuccessStatusCode();
        saved.Headers.ETag!.Tag.Should().Be("\"1\"");
        (await admin.PutAsJsonAsync(path + "?version=1", Content() with { Subject = "x\r\nInjected: y" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        foreach (string format in new[] { "draft", "html", "eml" })
        { (await denied.GetAsync(path + "?version=1&format=" + format)).StatusCode.Should().Be(HttpStatusCode.Forbidden); }
        (await admin.GetFromJsonAsync<AnnouncementContent>(path))!.To.Should().HaveCount(1);
        (await admin.GetFromJsonAsync<AnnouncementContent>(path))!.Cc.Should().Equal("copy@example.invalid");
        string html = await admin.GetStringAsync(path + "?version=1&format=html");
        html.Should().Contain("data:image/png;base64,").And.Contain("&amp;lt;b&amp;gt;").And.NotContain("<script>");
        using HttpResponseMessage download = await admin.GetAsync(path + "?version=1&format=eml");
        download.EnsureSuccessStatusCode();
        download.Content.Headers.ContentType!.MediaType.Should().Be("message/rfc822");
        download.Content.Headers.ContentDisposition!.FileNameStar.Should().EndWith("-v1.eml");
        using MimeMessage message = await MimeMessage.LoadAsync(await download.Content.ReadAsStreamAsync());
        message.Subject.Should().Be(Content().Subject);
        message.From.Mailboxes.Single().Address.Should().Be("actor@example.invalid");
        message.HtmlBody.Should().Contain("cid:banner");
        message.TextBody.Should().Contain(Content().Description);
        MimePart image = message.BodyParts.OfType<MimePart>().Single(p => p.ContentId == "banner");
        using var imageBytes = new MemoryStream();
        await image.Content!.DecodeToAsync(imageBytes);
        html.Should().Contain(Convert.ToBase64String(imageBytes.ToArray()));
        IConfigurationRoot config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:SecureOpsDb"] = connection }).Build();
        var store = new SqlAnnouncementStore(config);
        Guid owner = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/access/me")).GetProperty("userId").GetGuid();
        AnnouncementDraft draft = (await store.GetAsync(id, owner, default))!;
        (await store.SaveAsync(draft with { Version = 2, Content = draft.Content with { Notes = new string('x', 140000) } }, "synthetic", default))
            .Should().Be("AnnouncementInvalid");
        draft.Version.Should().Be(1);
        draft.Origin.Should().Be("Manual");
        (await new SqlAnnouncementStore(config).GetAsync(id, owner, default)).Should().BeEquivalentTo(draft);
        (await store.GetAsync(id, Guid.NewGuid(), default)).Should().BeNull();
        (await store.SaveAsync(draft with { OwnerId = Guid.NewGuid(), Version = 2 }, "synthetic", default)).Should().Be("AnnouncementNotFound");
        HttpResponseMessage[] races = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => admin.PutAsJsonAsync(path + "?version=1", Content())));
        races.Count(r => r.IsSuccessStatusCode).Should().Be(1);
        races.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
        (await admin.GetAsync(path + "?version=1&format=eml")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var sql = new SqlConnection(connection);
        (await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM announcements.DraftRevisions WHERE Id=@id", new { id })).Should().Be(2);
        string[] actions = (await sql.QueryAsync<string>("SELECT Action FROM audit.AuditLog WHERE JSON_VALUE(CASE WHEN ISJSON(DetailsJson)=1 THEN DetailsJson ELSE '{}' END,'$.Id')=@id", new { id = id.ToString() })).ToArray();
        actions.Count(a => a == "AnnouncementDraftRead").Should().Be(3);
        actions.Where(a => a != "AnnouncementDraftRead").Should().BeEquivalentTo(["AnnouncementDraftSaved", "AnnouncementDownloadPrepared", "AnnouncementDraftSaved"]);
        foreach (string route in new[] { "/api/v1/announcements", "/api/v1/announcements/banners" })
        {
            (await anonymous.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await denied.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        await FluentActions.Awaiting(() => sql.ExecuteAsync("UPDATE announcements.DraftRevisions SET Version=99 WHERE Id=@id", new { id })).Should().ThrowAsync<SqlException>();
        string trigger = "TR_OcoTest_" + Guid.NewGuid().ToString("N");
        await sql.ExecuteAsync($"CREATE TRIGGER audit.[{trigger}] ON audit.AuditLog AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Action LIKE 'Announcement%') THROW 51149, 'Synthetic failure.', 1; END;");
        try
        {
            (await admin.PutAsJsonAsync(path + "?version=2", Content())).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await admin.GetAsync(path + "?version=2&format=eml")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await admin.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await admin.GetAsync(path + "?version=2&format=html")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await admin.GetAsync("/api/v1/announcements")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await admin.GetAsync("/api/v1/announcements/banners")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await store.GetAsync(id, owner, default))!.Version.Should().Be(2);
        }
        finally { await sql.ExecuteAsync($"DROP TRIGGER audit.[{trigger}];"); }
        File.Copy(Path.Combine(Assets(SKEncodedImageFormat.Jpeg), "banner.bin"), Path.Combine(assets, "banner.bin"), true);
        (await admin.GetAsync(path + "?version=2&format=html")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.GetAsync(path)).EnsureSuccessStatusCode();
        string incomplete = "/api/v1/announcements/" + Guid.NewGuid();
        (await admin.PutAsJsonAsync(incomplete, Content() with { Subject = "", To = [] })).EnsureSuccessStatusCode();
        using HttpResponseMessage missing = await admin.GetAsync(incomplete + "?version=1&format=eml");
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonElement problem = (await missing.Content.ReadFromJsonAsync<JsonElement>());
        problem.GetProperty("code").GetString().Should().Be("AnnouncementIncomplete");
        problem.GetProperty("fields").EnumerateArray().Select(f => f.GetString()).Should().Contain(["Subject", "To"]);
        using HttpResponseMessage listed = await admin.GetAsync("/api/v1/announcements?page=1&pageSize=1&ownerId=" + Guid.NewGuid());
        listed.EnsureSuccessStatusCode();
        listed.Headers.CacheControl!.NoStore.Should().BeTrue();
        AnnouncementPage page = (await listed.Content.ReadFromJsonAsync<AnnouncementPage>())!;
        page.Total.Should().Be(2);
        page.Items.Single().MissingFieldCount.Should().Be(2);
        (await admin.GetFromJsonAsync<AnnouncementPage>("/api/v1/announcements?page=2&pageSize=1"))!.Items.Single().Version.Should().Be(2);
        foreach (string invalid in new[] { "page=0", "pageSize=101", "page=10001", "pageSize=0" })
        { (await admin.GetAsync("/api/v1/announcements?" + invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest); }
        (await admin.GetAsync(path + "?format=list")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        output.WriteLine("HTTP list page bytes={0}", (await listed.Content.ReadAsByteArrayAsync()).Length);
        (await admin.GetFromJsonAsync<AnnouncementBanner[]>("/api/v1/announcements/banners"))!.Single().State.Should().Be("PresentNotValidated");
        await FinalApiAsync(admin, denied, store, owner);
        await TransientApiAsync(admin, denied, anonymous, sql, path);
        File.Move(Path.Combine(assets, "banner.bin"), Path.Combine(assets, "banner.retained"));
        (await admin.GetFromJsonAsync<AnnouncementBanner[]>("/api/v1/announcements/banners"))!.Single().State.Should().Be("Missing");
        using HttpResponseMessage absent = await admin.GetAsync(path + "?version=2&format=eml");
        absent.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await absent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("AnnouncementAssetMissing");
        (await admin.GetFromJsonAsync<AnnouncementBanner[]>("/api/v1/announcements/banners?templateRevision=oco-table-v2"))!.Single().State.Should().Be("Missing");
    }
}
