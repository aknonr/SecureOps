using System.Globalization;
using System.Net;
using SecureOps.Domain.Announcements;
using SkiaSharp;

namespace SecureOps.Infrastructure.Announcements;

public sealed partial class AnnouncementRenderer
{
    private static string Lines(string value) => WebUtility.HtmlEncode(value).Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace("\r", "\n", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);

    /// <summary>Inert draft-only fallback; no missing-image requests or claim of prepared branding.</summary>
    public static string RenderUnbranded(AnnouncementContent content) =>
        "<!doctype html><html lang=\"tr\"><head><meta charset=\"utf-8\"></head><body style=\"font:16px Arial,sans-serif\">"
        + "<h1 style=\"font-size:20px\">Taslak önizleme</h1><p>Görseller henüz doğrulanmadı. Bu görünüm gönderime hazır değildir.</p>"
        + string.Concat(AnnouncementValidation.Fields(content).Select(f => "<h2 style=\"font-size:16px\">" + Label(f.Key)
            + "</h2><p>" + (string.IsNullOrWhiteSpace(f.Value) ? "Henüz girilmedi" : Lines(f.Value)) + "</p>")) + "</body></html>";

    private static string RenderTableV3((string Label, string Value)[] fields, AnnouncementPresentation presentation, bool inline)
    {
        string Image(string role, int width)
        {
            AnnouncementImage asset = presentation.Images.Single(image => image.Role == role);
            using var stream = new SKMemoryStream(asset.Bytes);
            using var codec = SKCodec.Create(stream);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
            { throw new InvalidOperationException("Invalid presentation image."); }
            int height = Math.Max(1, (int)Math.Round((double)width * codec.Info.Height / codec.Info.Width));
            string source = inline ? $"data:image/{asset.Type};base64,{Convert.ToBase64String(asset.Bytes)}" : "cid:" + role;
            string alt = role switch { "header" => "Planlı çalışma duyurusu", "main" => "Planlı bakım", "logo" => "Kurum logosu", _ => role };
            return $"<img alt=\"{alt}\" src=\"{WebUtility.HtmlEncode(source)}\" width=\"{width}\" height=\"{height}\" border=\"0\" "
                + $"style=\"display:block;border:0;width:{width}px;max-width:100%;height:auto\">";
        }
        string rows = string.Concat(fields.Select((field, index) =>
            $"<tr bgcolor=\"{(index % 2 == 0 ? "#f2f2f2" : "#ffffff")}\"><th scope=\"row\" width=\"220\" align=\"left\" valign=\"top\" "
            + "style=\"width:37%;padding:12px;border:1px solid #bfbfbf;font: bold 14px Arial,sans-serif;line-height:21px;word-wrap:break-word\">" + field.Label
            + "</th><td width=\"380\" align=\"left\" valign=\"top\" style=\"width:63%;padding:12px;border:1px solid #bfbfbf;"
            + "font:14px Arial,sans-serif;line-height:21px;word-wrap:break-word\">" + Lines(field.Value) + "</td></tr>"));
        string social = string.Concat(_bundleRoles.Skip(3).Select(role => "<td width=\"36\" valign=\"middle\">" + Image(role, 24) + "</td>"));
        return "<!doctype html><html lang=\"tr\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
            + "</head><body style=\"margin:0;padding:0;background:#ffffff;color:#171717\">"
            + "<table role=\"presentation\" width=\"100%\" border=\"0\" cellspacing=\"0\" cellpadding=\"0\"><tr><td align=\"center\">"
            + "<!--[if mso]><table role=\"presentation\" width=\"600\" align=\"center\" cellpadding=\"0\" cellspacing=\"0\"><tr><td><![endif]-->"
            + "<table role=\"presentation\" width=\"600\" align=\"center\" border=\"0\" cellspacing=\"0\" cellpadding=\"0\" style=\"width:100%;max-width:600px;table-layout:fixed;border-collapse:collapse\">"
            + "<tr><td align=\"center\">" + Image("header", 600) + "</td></tr><tr><td align=\"center\">" + Image("main", 600)
            + "</td></tr><tr><td align=\"center\" style=\"padding:20px 12px;font:bold 18px Arial,sans-serif\">PLANLI ÇALIŞMA DUYURUSU</td></tr>"
            + "<tr><td><table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;table-layout:fixed;border-collapse:collapse\">"
            + rows + "</table></td></tr><tr><td style=\"padding:20px 12px;font:14px Arial,sans-serif;line-height:21px\">" + Lines(presentation.Footer)
            + "</td></tr><tr><td style=\"padding:12px\"><table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\"><tr><td width=\"180\">"
            + Image("logo", 150) + "</td>" + social + "</tr></table></td></tr></table><!--[if mso]></td></tr></table><![endif]-->"
            + "</td></tr></table></body></html>";
    }
}
