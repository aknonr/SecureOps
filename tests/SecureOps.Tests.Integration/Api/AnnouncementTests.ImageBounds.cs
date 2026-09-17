using FluentAssertions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Announcements;
using SecureOps.Shared.Configuration;
using SkiaSharp;

namespace SecureOps.Tests.Integration.Api;

public sealed partial class AnnouncementTests
{
    [Fact]
    public async Task ImageBounds_RejectOversizeDecodedDimensionsAndAggregateBudget()
    {
        string root = Assets();
        var options = new AnnouncementOptions
        {
            AssetDirectory = root,
            Banners = new() { ["asset"] = "banner.bin" },
            Bundles = new() { ["bundle-v1"] = new() { Label = "Synthetic", Footer = "Synthetic", Assets = _roles.ToDictionary(r => r, _ => "asset") } }
        };
        var renderer = new AnnouncementRenderer(Options.Create(options));
        string file = Path.Combine(root, "banner.bin");
        await File.WriteAllBytesAsync(file, new byte[1024 * 1024 + 1]);
        await FluentActions.Awaiting(() => renderer.AssetAsync("asset", default)).Should().ThrowAsync<InvalidOperationException>();
        using (var wide = new SKBitmap(2049, 1))
        using (SKData encoded = wide.Encode(SKEncodedImageFormat.Png, 100))
        { await File.WriteAllBytesAsync(file, encoded.ToArray()); }
        await FluentActions.Awaiting(() => renderer.AssetAsync("asset", default)).Should().ThrowAsync<InvalidOperationException>();
        using var bitmap = new SKBitmap(512, 256, SKColorType.Bgra8888, SKAlphaType.Opaque);
        byte[] noise = new byte[bitmap.ByteCount];
        new Random(17).NextBytes(noise);
        for (int offset = 3; offset < noise.Length; offset += 4)
        { noise[offset] = 255; }
        System.Runtime.InteropServices.Marshal.Copy(noise, 0, bitmap.GetPixels(), noise.Length);
        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        byte[] bytes = data.ToArray();
        bytes.Length.Should().BeInRange(350_000, 1024 * 1024);
        await File.WriteAllBytesAsync(file, bytes);
        (await renderer.AssetAsync("asset", default)).Type.Should().Be("png");
        await FluentActions.Awaiting(() => renderer.PresentationAsync(FinalContent(), default)).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Presentation image budget exceeded.");
    }
}
