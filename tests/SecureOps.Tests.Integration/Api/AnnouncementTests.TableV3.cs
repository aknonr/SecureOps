using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using NSubstitute;
using SecureOps.Domain.Access;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Announcements;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Integration.Api;

public sealed partial class AnnouncementTests
{
    [Fact]
    public async Task TableV3_BoundsOutlookMarkup_StacksArtwork_PreservesV2AndMimeBytes()
    {
        var config = new AnnouncementOptions
        {
            AssetDirectory = Assets(),
            Banners = new() { ["asset"] = "banner.bin" },
            Bundles = new() { ["bundle-v1"] = new() { Footer = "Synthetic footer", Assets = _roles.ToDictionary(r => r, _ => "asset") } }
        };
        var renderer = new AnnouncementRenderer(Options.Create(config));
        AnnouncementContent oldContent = FinalContent();
        AnnouncementPresentation oldPresentation = await renderer.PresentationAsync(oldContent, TestContext.Current.CancellationToken);
        var oldDraft = new AnnouncementDraft(Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UtcNow, oldContent,
            "sender@example.invalid", oldPresentation.Hash, TemplateRevision: oldContent.TemplateRevision);
        byte[] oldBytes = await AnnouncementRenderer.EmailAsync(oldDraft, oldPresentation, TestContext.Current.CancellationToken);
        AnnouncementContent content = oldContent with { TemplateRevision = "oco-table-v3", Description = "First line\nSecond <line>" };
        AnnouncementPresentation presentation = await renderer.PresentationAsync(content, TestContext.Current.CancellationToken);
        presentation.Hash.Should().NotBe(oldPresentation.Hash);
        AnnouncementDraft draft = oldDraft with { Content = content, TemplateRevision = content.TemplateRevision, BannerHash = presentation.Hash };
        using var mail = MimeMessage.Load(new MemoryStream(await AnnouncementRenderer.EmailAsync(draft, presentation, TestContext.Current.CancellationToken)), TestContext.Current.CancellationToken);
        string html = mail.HtmlBody!;
        html.Should().Contain("<!--[if mso]>").And.Contain("width=\"600\" align=\"center\"").And.NotContain("<main").And.NotContain("white-space:pre-wrap");
        html.Should().Contain("First line<br>Second &lt;line&gt;");
        int header = html.IndexOf("cid:header", StringComparison.Ordinal), main = html.IndexOf("cid:main", StringComparison.Ordinal);
        html[header..main].Should().Contain("</td></tr><tr><td align=\"center\">");
        main.Should().BeGreaterThan(header);
        html.IndexOf("PLANLI ÇALIŞMA DUYURUSU", StringComparison.Ordinal).Should().BeGreaterThan(main);
        foreach (string service in Services())
        { html.Should().Contain(WebUtility.HtmlEncode(service)); }
        foreach (AnnouncementImage image in presentation.Images)
        {
            MimePart part = mail.BodyParts.OfType<MimePart>().Single(p => p.ContentId == image.Role);
            using var bytes = new MemoryStream();
            part.Content!.DecodeTo(bytes, TestContext.Current.CancellationToken);
            bytes.ToArray().Should().Equal(image.Bytes);
        }
        using var oldMail = MimeMessage.Load(new MemoryStream(oldBytes), TestContext.Current.CancellationToken);
        using var repeatedOldMail = MimeMessage.Load(new MemoryStream(await AnnouncementRenderer.EmailAsync(oldDraft, oldPresentation, TestContext.Current.CancellationToken)), TestContext.Current.CancellationToken);
        repeatedOldMail.HtmlBody.Should().Be(oldMail.HtmlBody);
        repeatedOldMail.TextBody.Should().Be(oldMail.TextBody);
        (await renderer.PresentationAsync(oldContent, TestContext.Current.CancellationToken)).Hash.Should().Be(oldPresentation.Hash);
    }

    [Fact]
    public async Task LiveDraft_WithoutAssets_RemainsInertAndExplicitlyIncomplete()
    {
        IOptions<AnnouncementOptions> options = Options.Create(new AnnouncementOptions { Enabled = true });
        IApplicationAccessService access = Substitute.For<IApplicationAccessService>();
        var user = new ApplicationUser(Guid.NewGuid(), "synthetic:operator", "test", AccessStatus.Approved,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, 1, [], [Capabilities.AnnouncementDrafts]);
        access.GetCurrentAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<AccessOperationContext>(), Arg.Any<CancellationToken>())
            .Returns(AccessServiceResult<EnsureAccessUserResult>.Success(new(user, null, false, false)));
        var service = new AnnouncementService(null!, new AnnouncementRenderer(options), access, options, NullLogger<AnnouncementService>.Instance);
        AnnouncementContent input = FinalContent() with { BannerRevision = "", TemplateRevision = "oco-table-v3", Subject = "", Description = "<script>inert</script>" };
        AnnouncementOutcome preview = await service.ExecuteAsync(new ClaimsPrincipal(), new("synthetic", "test", null), Guid.Empty, 0, "live", input, 1, 25, TestContext.Current.CancellationToken);
        preview.Error.Should().BeNull();
        preview.Fields.Should().Contain("Subject").And.Contain("BannerRevision");
        preview.Html.Should().Contain("Taslak önizleme").And.Contain("&lt;script&gt;").And.NotContain("<script>").And.NotContain("<img");
        input.Description.Should().Be("<script>inert</script>");
    }
}
