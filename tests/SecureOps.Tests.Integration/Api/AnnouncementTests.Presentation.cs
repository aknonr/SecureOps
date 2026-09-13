using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MimeKit;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Announcements;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Integration.Api;

// Other SQL fixtures temporarily install database-wide failure triggers.
[CollectionDefinition("Announcement SQL", DisableParallelization = true)]
public sealed class AnnouncementSqlCollection;

public sealed partial class AnnouncementTests
{
    private static readonly string[] _roles = ["header", "main", "logo", "linkedin", "instagram", "youtube"];
    private static string[] Services() => Enumerable.Range(1, 155).Select(i => $"Sentetik servis {i:000} - Türkçe & inceleme <b> uygulama hizmeti").ToArray();
    private static AnnouncementContent FinalContent() => Content() with
    { TemplateRevision = "oco-table-v2", BannerRevision = "bundle-v1", AffectedServices = Services() };
    [Fact]
    public void ServiceBounds_PreserveLegacyDefaultsAndRejectSilentLoss()
    {
        string.Join("\n", Services()).Length.Should().BeGreaterThan(7114);
        AnnouncementValidation.Errors(FinalContent(), true).Should().BeEmpty();
        foreach (string[] invalid in new[] { new string[257], new[] { "x\nInjected" }, new[] { new string('x', 257) }, Enumerable.Repeat(new string('x', 100), 160).ToArray() })
        { AnnouncementValidation.Errors(FinalContent() with { AffectedServices = invalid }, false).Should().Contain("AffectedServices"); }
        AnnouncementValidation.Errors(FinalContent() with { TemplateRevision = "oco-v1" }, false).Should().Contain("AffectedServices");
        AnnouncementValidation.Errors(FinalContent() with { AffectedServices = [] }, true).Should().Contain("AffectedServices");
        AnnouncementValidation.Errors(FinalContent() with { AffectedServices = [] }, false).Should().BeEmpty();
        System.Text.Json.Nodes.JsonObject json = JsonSerializer.SerializeToNode(Content())!.AsObject();
        json.Remove("TemplateRevision");
        json.Remove("AffectedServices");
        AnnouncementContent legacy = json.Deserialize<AnnouncementContent>()!;
        legacy.TemplateRevision.Should().Be("oco-v1");
        legacy.AffectedServices.Should().BeNull();
    }
    [Fact]
    public async Task Bundle_MimePreviewHashesAndFooterAreConsistent()
    {
        var config = new AnnouncementOptions
        {
            AssetDirectory = Assets(),
            Banners = new() { ["asset"] = "banner.bin" },
            Bundles = new() { ["bundle-v1"] = new() { Label = "Yerel paket", Footer = "Sentetik altbilgi & <script>inert</script>", Assets = _roles.ToDictionary(r => r, _ => "asset") } }
        };
        var renderer = new AnnouncementRenderer(Options.Create(config));
        using (var locked = new FileStream(Path.Combine(config.AssetDirectory, "banner.bin"), FileMode.Open, FileAccess.Read, FileShare.None))
        { renderer.Bundles(default).Single().State.Should().Be("PresentNotValidated"); }
        AnnouncementPresentation presentation = await renderer.PresentationAsync(FinalContent(), default);
        var draft = new AnnouncementDraft(Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UtcNow, FinalContent(), "sender@example.invalid",
            presentation.Hash, TemplateRevision: "oco-table-v2");
        (string html, string text) = AnnouncementRenderer.RenderPresentation(draft, presentation, true);
        html.Should().NotContain("<script").And.NotContain("https:").And.Contain("&amp;lt;b&amp;gt;");
        foreach (string service in Services())
        { text.Should().Contain(service); html.Should().Contain(WebUtility.HtmlEncode(service)); }
        string[] headings = ["Duyuru Tarihi", "Çalışma Kayıt Numarası", "Çalışma Yapılacak Sistem/Uygulama", "Çalışmanın Başlangıç", "Çalışma Bitiş", "Çalışmanın Açıklaması", "Çalışmanın Etki Detayı", "Çalışmadan Etkilenen Servisler", "Notlar/Özel Durumlar"];
        headings.Select(h => html.IndexOf(h, StringComparison.Ordinal)).Should().BeInAscendingOrder();
        using var mail = MimeMessage.Load(new MemoryStream(await AnnouncementRenderer.EmailAsync(draft, presentation, default)));
        mail.TextBody.Should().NotBeNull();
        mail.TextBody!.ReplaceLineEndings("\n").Should().Be(text.ReplaceLineEndings("\n"));
        foreach (AnnouncementImage asset in presentation.Images)
        {
            MimePart part = mail.BodyParts.OfType<MimePart>().Single(p => p.ContentId == asset.Role);
            using var bytes = new MemoryStream();
            part.Content!.DecodeTo(bytes);
            bytes.ToArray().Should().Equal(asset.Bytes);
            mail.HtmlBody.Should().Contain("cid:" + asset.Role);
            html.Should().Contain(Convert.ToBase64String(bytes.ToArray()));
        }
        mail.BodyParts.OfType<MimePart>().Count(p => p.ContentId is not null).Should().Be(6);
        config.Bundles["bundle-v1"].Footer += " amended";
        (await renderer.PresentationAsync(FinalContent(), default)).Hash.Should().NotBe(presentation.Hash);
        config.Bundles["bundle-v1"].Assets.Remove("logo");
        renderer.Bundles(default).Single().State.Should().Be("Invalid");
        await FluentActions.Awaiting(() => renderer.PresentationAsync(FinalContent(), default)).Should().ThrowAsync<InvalidOperationException>();
    }

    private static async Task FinalApiAsync(HttpClient admin, HttpClient denied, SqlAnnouncementStore store, Guid owner)
    {
        string path = "/api/v1/announcements/" + Guid.NewGuid();
        (await admin.PutAsJsonAsync(path, FinalContent())).EnsureSuccessStatusCode();
        AnnouncementContent saved = (await admin.GetFromJsonAsync<AnnouncementContent>(path))!;
        saved.Should().BeEquivalentTo(FinalContent() with { To = ["reader@example.invalid"], Cc = ["copy@example.invalid"] });
        var id = Guid.Parse(path.Split('/').Last());
        AnnouncementDraft draft = (await store.GetAsync(id, owner, default))!;
        draft.TemplateRevision.Should().Be("oco-table-v2");
        draft.Origin.Should().Be("Manual");
        using HttpResponseMessage download = await admin.GetAsync(path + "?version=1&format=eml");
        download.EnsureSuccessStatusCode();
        using MimeMessage mail = await MimeMessage.LoadAsync(await download.Content.ReadAsStreamAsync());
        mail.TextBody.Should().Contain(Services()[^1]);
        mail.BodyParts.OfType<MimePart>().Count(p => p.ContentId is not null).Should().Be(6);
        (await store.GetAsync(id, owner, default))!.Version.Should().Be(1);
        (await admin.PutAsJsonAsync(path, FinalContent())).StatusCode.Should().Be(HttpStatusCode.Conflict);
        foreach (string format in new[] { "draft", "html", "eml" })
        { (await denied.GetAsync(path + "?version=1&format=" + format)).StatusCode.Should().Be(HttpStatusCode.Forbidden); }
        (await store.GetAsync(id, Guid.NewGuid(), default)).Should().BeNull();
    }
}
