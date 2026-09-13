using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using SecureOps.Domain.Announcements;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Announcements;

namespace SecureOps.Infrastructure.Announcements;

/// <summary>Bounded verified bytes for one deterministic role.</summary>
public sealed record AnnouncementImage(string Role, byte[] Bytes, string Type, string Hash);
/// <summary>One action's validated presentation; no global or authorization cache.</summary>
public sealed record AnnouncementPresentation(string Hash, string Footer, IReadOnlyList<AnnouncementImage> Images);

public sealed partial class AnnouncementRenderer
{
    private static readonly string[] _bundleRoles = ["header", "main", "logo", "linkedin", "instagram", "youtube"];
    private AnnouncementAssetBundle Bundle(string revision)
    {
        if (options.Value.Bundles.Count > 16 || !options.Value.Bundles.TryGetValue(revision, out AnnouncementAssetBundle? bundle)
            || bundle is null || bundle.Assets is null || bundle.Assets.Count != 6 || _bundleRoles.Except(bundle.Assets.Keys, StringComparer.Ordinal).Any()
            || string.IsNullOrWhiteSpace(bundle.Footer) || bundle.Footer.Length > 2000
            || bundle.Footer.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
        { throw new InvalidOperationException("Presentation bundle invalid."); }
        return bundle;
    }

    /// <summary>Metadata-only bundle discovery; exact six roles, no paths or image bytes.</summary>
    public IReadOnlyList<AnnouncementBanner> Bundles(CancellationToken token)
    {
        if (options.Value.Bundles.Count > 16 || options.Value.Bundles.Keys.Any(k => !Regex.IsMatch(k, @"\A[a-z0-9-]{1,64}\z")))
        { throw new InvalidOperationException("Bundle catalogue invalid."); }
        IReadOnlyDictionary<string, AnnouncementBanner> assets = Banners(token).ToDictionary(b => b.Revision);
        return options.Value.Bundles.OrderBy(b => b.Key, StringComparer.Ordinal).Select(pair =>
        {
            token.ThrowIfCancellationRequested();
            string label = pair.Value?.Label ?? pair.Key;
            if (string.IsNullOrWhiteSpace(label) || label.Length > 80 || label.Any(c => char.IsControl(c) || c is '<' or '>'))
            { label = pair.Key; }
            string state;
            try
            {
                string[] states = _bundleRoles.Select(role => assets.GetValueOrDefault(Bundle(pair.Key).Assets[role])?.State ?? "Missing").ToArray();
                state = new[] { "Missing", "Unavailable", "Invalid" }.FirstOrDefault(states.Contains) ?? "PresentNotValidated";
            }
            catch (InvalidOperationException) { state = "Invalid"; }
            return new AnnouncementBanner(pair.Key, label, state);
        }).ToArray();
    }

    /// <summary>Sequential bounded validation: at most six 256-KiB images; footer and ordered roles are fingerprinted.</summary>
    public async Task<AnnouncementPresentation> PresentationAsync(AnnouncementContent content, CancellationToken token)
    {
        if (content.TemplateRevision == "oco-v1")
        {
            (byte[] bytes, string type, string hash) = await AssetAsync(content.BannerRevision, token);
            return new(hash, "", [new("banner", bytes, type, hash)]);
        }
        if (content.TemplateRevision != "oco-table-v2")
        { throw new InvalidOperationException("Unknown template."); }
        AnnouncementAssetBundle bundle = Bundle(content.BannerRevision);
        List<AnnouncementImage> images = [];
        foreach (string role in _bundleRoles)
        {
            token.ThrowIfCancellationRequested();
            (byte[] bytes, string type, string hash) = await AssetAsync(bundle.Assets[role], token);
            images.Add(new(role, bytes, type, hash));
        }
        byte[] identity = JsonSerializer.SerializeToUtf8Bytes(new
        {
            content.TemplateRevision,
            content.BannerRevision,
            bundle.Footer,
            Assets = images.Select(i => new { i.Role, Revision = bundle.Assets[i.Role], i.Type, i.Hash })
        });
        string presentationHash = Convert.ToHexString(SHA256.HashData(identity));
        if (content.DateTextRevision != "iso-v1")
        { presentationHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(presentationHash + "\n" + content.DateTextRevision))); }
        return new(presentationHash, bundle.Footer, images);
    }

    /// <summary>Version-specific, escaped table rendering; all affected services appear in both alternatives.</summary>
    public static (string Html, string Text) RenderPresentation(AnnouncementDraft draft, AnnouncementPresentation presentation, bool inline)
    {
        string Source(AnnouncementImage image) => inline ? $"data:image/{image.Type};base64,{Convert.ToBase64String(image.Bytes)}" : "cid:" + image.Role;
        if (draft.TemplateRevision == "oco-v1")
        { return Render(draft, Source(presentation.Images.Single())); }
        if (draft.TemplateRevision != "oco-table-v2" || !presentation.Images.Select(i => i.Role).SequenceEqual(_bundleRoles))
        { throw new InvalidOperationException("Unknown presentation."); }
        AnnouncementContent c = draft.Content;
        string DateText(string value)
        {
            if (c.DateTextRevision == "iso-v1" || value.Length == 0)
            { return value; }
            if (c.DateTextRevision != "tr-v1")
            { throw new InvalidOperationException("Unknown date presentation."); }
            if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day))
            { return day.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture); }
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset time)
                ? time.ToString("dd.MM.yyyy HH:mm:ss 'UTC' zzz", CultureInfo.InvariantCulture) : value;
        }
        (string Label, string Value)[] fields = [("Duyuru Tarihi", DateText(c.AnnouncementDate)), ("Çalışma Kayıt Numarası", c.OcoReference),
            ("Çalışma Yapılacak Sistem/Uygulama", c.Scope), ("Çalışmanın Başlangıç Tarihi/Saati", DateText(c.WorkStart)),
            ("Çalışma Bitiş Tarihi/Saati", DateText(c.WorkEnd)), ("Çalışmanın Açıklaması", c.Description), ("Çalışmanın Etki Detayı", c.Impact),
            ("Çalışmadan Etkilenen Servisler", string.Join("\n", c.AffectedServices ?? [])),
            ("Notlar/Özel Durumlar", string.Join("\n\n", new[] { c.Checks, c.Notes }.Where(s => !string.IsNullOrEmpty(s))))];
        if (!string.IsNullOrEmpty(c.RestartStart))
        { fields = [.. fields, ("Yeniden başlatma başlangıcı", DateText(c.RestartStart)), ("Yeniden başlatma bitişi", DateText(c.RestartEnd ?? ""))]; }
        string Image(string role, int width) => "<img alt=\"" + role + "\" width=\"" + width + "\" style=\"max-width:100%;height:auto\" src=\""
            + WebUtility.HtmlEncode(Source(presentation.Images.Single(i => i.Role == role))) + "\">";
        string rows = string.Concat(fields.Select((f, i) => "<tr style=\"background:" + (i % 2 == 0 ? "#f2f2f2" : "#fff")
            + "\"><th scope=\"row\" style=\"width:40%;padding:12px;border:1px solid #bfbfbf\">" + f.Label
            + "</th><td style=\"padding:12px;border:1px solid #bfbfbf;white-space:pre-wrap;overflow-wrap:anywhere\">"
            + WebUtility.HtmlEncode(f.Value) + "</td></tr>"));
        string html = "<!doctype html><html lang=\"tr\"><meta charset=\"utf-8\"><body style=\"margin:0;background:#fff;color:#0a0a0a;font:14px Arial,sans-serif\">"
            + "<main style=\"max-width:600px;margin:auto\">" + Image("header", 600) + Image("main", 600)
            + "<h1 style=\"font-size:18px;text-align:center\">PLANLI ÇALIŞMA DUYURUSU</h1>"
            + "<table style=\"width:100%;table-layout:fixed;border-collapse:collapse;text-align:center\">" + rows + "</table>"
            + "<footer><p style=\"white-space:pre-wrap\">" + WebUtility.HtmlEncode(presentation.Footer) + "</p>" + Image("logo", 150)
            + string.Concat(_bundleRoles.Skip(3).Select(role => Image(role, 24))) + "</footer></main></body></html>";
        return (html, string.Join("\r\n\r\n", fields.Select(f => f.Label + ": " + f.Value)) + "\r\n\r\n" + presentation.Footer);
    }
}
