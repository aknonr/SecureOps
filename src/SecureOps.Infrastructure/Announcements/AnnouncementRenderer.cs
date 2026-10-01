using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using MimeKit;
using SecureOps.Domain.Announcements;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Announcements;
using SkiaSharp;

namespace SecureOps.Infrastructure.Announcements;

/// <summary>Allowlisted private assets and one escaped, non-network template pipeline.</summary>
public sealed partial class AnnouncementRenderer(IOptions<AnnouncementOptions> options)
{
    internal const int MaxAssetBytes = 1024 * 1024;
    internal const int MaxPresentationBytes = 2 * 1024 * 1024;
    // Only 32 successful content-hash/type receipts; never paths, bytes, drafts or access decisions.
    private readonly Dictionary<string, string> _validatedImages = [];
    private string AssetPath(string revision)
    {
        AnnouncementOptions config = options.Value;
        if (!config.Banners.TryGetValue(revision, out string? name) || string.IsNullOrEmpty(name) || name != Path.GetFileName(name) || name.Contains(':')
            || !Path.IsPathFullyQualified(config.AssetDirectory) || config.AssetDirectory.StartsWith("\\\\", StringComparison.Ordinal)
            || config.AssetDirectory.StartsWith("//", StringComparison.Ordinal))
        { throw new InvalidOperationException("Banner unavailable."); }
        string path = Path.Combine(config.AssetDirectory, name);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(config.AssetDirectory) & FileAttributes.ReparsePoint) != 0)
        { throw new InvalidOperationException("Banner unavailable."); }
        return path;
    }

    /// <summary>At most 32 allowlisted choices; metadata I/O only, no filesystem enumeration or codec/cache.</summary>
    public IReadOnlyList<AnnouncementBanner> Banners(CancellationToken token = default)
    {
        if (options.Value.Banners.Count > 32 || options.Value.Banners.Keys.Any(k =>
            !System.Text.RegularExpressions.Regex.IsMatch(k, @"\A[a-z0-9-]{1,64}\z")))
        { throw new InvalidOperationException("Banner catalogue invalid."); }
        return options.Value.Banners.Keys.Order(StringComparer.Ordinal).Select(revision =>
        {
            token.ThrowIfCancellationRequested();
            string? label = options.Value.BannerLabels.GetValueOrDefault(revision);
            if (string.IsNullOrWhiteSpace(label) || label.Length > 80 || label.Any(c => char.IsControl(c) || c is '<' or '>'))
            { label = revision; }
            string state;
            try
            { state = new FileInfo(AssetPath(revision)).Length is >= 24 and <= MaxAssetBytes ? "PresentNotValidated" : "Invalid"; }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { state = "Missing"; }
            catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException) { state = "Unavailable"; }
            return new AnnouncementBanner(revision, label, state);
        }).ToArray();
    }

    /// <summary>Reads at most 1 MiB; codec checks type, dimensions and complete decode before use.</summary>
    public async Task<(byte[] Bytes, string Type, string Hash)> AssetAsync(string revision, CancellationToken token)
    {
        string path = AssetPath(revision);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is < 24 or > MaxAssetBytes)
        { throw new InvalidOperationException("Banner size invalid."); }
        byte[] bytes = new byte[(int)file.Length];
        await file.ReadExactlyAsync(bytes, token);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        lock (_validatedImages)
        {
            if (_validatedImages.TryGetValue(hash, out string? knownType))
            { return (bytes, knownType, hash); }
        }
        using var stream = new SKMemoryStream(bytes);
        using var codec = SKCodec.Create(stream);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg)
            || codec.Info.Width is < 1 or > 2048 || codec.Info.Height is < 1 or > 1024 || codec.FrameCount > 1)
        { throw new InvalidOperationException("Banner type or dimensions invalid."); }
        using var bitmap = new SKBitmap(codec.Info);
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
        { throw new InvalidOperationException("Banner decode failed."); }
        string type = codec.EncodedFormat == SKEncodedImageFormat.Png ? "png" : "jpeg";
        lock (_validatedImages)
        {
            if (_validatedImages.Count >= 32)
            { _validatedImages.Remove(_validatedImages.Keys.First()); }
            _validatedImages[hash] = type;
        }
        return (bytes, type, hash);
    }

    /// <summary>Returns inert HTML and text; source/user text is never HTML-decoded.</summary>
    public static (string Html, string Text) Render(AnnouncementDraft draft, string image)
    {
        (string Key, string Value)[] fields = [.. AnnouncementValidation.Fields(draft.Content),
            ("RestartStart", draft.Content.RestartStart ?? ""), ("RestartEnd", draft.Content.RestartEnd ?? "")];
        string text = string.Join("\r\n\r\n", fields.Select(f => Label(f.Key) + ": " + f.Value));
        string html = "<!doctype html><html lang=\"tr\"><meta charset=\"utf-8\"><body><img alt=\"Planlı çalışma\" style=\"max-width:100%\" src=\""
            + WebUtility.HtmlEncode(image) + "\"><main>" + string.Concat(fields.Select(f => "<section><h2>" + Label(f.Key)
                + "</h2><p style=\"white-space:pre-wrap\">" + WebUtility.HtmlEncode(f.Value) + "</p></section>")) + "</main></body></html>";
        return (html, text);
    }
    private static string Label(string key) => key switch
    {
        "OcoReference" => "OCO",
        "Scope" => "Kapsam",
        "Subject" => "Konu",
        "AnnouncementDate" => "Duyuru tarihi",
        "WorkStart" => "Çalışma başlangıcı",
        "WorkEnd" => "Çalışma bitişi",
        "Description" => "Açıklama",
        "Impact" => "Etki",
        "Checks" => "Kontroller",
        "Notes" => "Notlar",
        "RestartStart" => "Yeniden başlatma başlangıcı",
        "RestartEnd" => "Yeniden başlatma bitişi",
        _ => key
    };

    /// <summary>Creates a draft email artifact, never invokes SMTP or Outlook.</summary>
    public static async Task<byte[]> EmailAsync(AnnouncementDraft draft, byte[] bytes, string type, CancellationToken token)
        => await EmailAsync(draft, new("", "", [new("banner", bytes, type, "")]), token);

    /// <summary>Uses exactly the same ordered assets and fields as the browser representation.</summary>
    public static async Task<byte[]> EmailAsync(AnnouncementDraft draft, AnnouncementPresentation presentation, CancellationToken token)
    {
        (string html, string text) = RenderPresentation(draft, presentation, false);
        using var message = new MimeMessage { Subject = draft.Content.Subject, Date = draft.SavedAt, MessageId = $"{draft.Id:N}.{draft.Version}@wasas.invalid" };
        message.From.Add(MailboxAddress.Parse(draft.Sender));
        foreach (string recipient in draft.Content.To)
        { message.To.Add(MailboxAddress.Parse(recipient)); }
        foreach (string recipient in draft.Content.Cc)
        { message.Cc.Add(MailboxAddress.Parse(recipient)); }
        var builder = new BodyBuilder { TextBody = text, HtmlBody = html };
        foreach (AnnouncementImage asset in presentation.Images)
        {
            MimeEntity image = builder.LinkedResources.Add(asset.Role + "." + asset.Type, asset.Bytes, new ContentType("image", asset.Type));
            image.ContentId = asset.Role;
        }
        message.Body = builder.ToMessageBody();
        using var output = new MemoryStream();
        await message.WriteToAsync(output, token);
        return output.ToArray();
    }
}
