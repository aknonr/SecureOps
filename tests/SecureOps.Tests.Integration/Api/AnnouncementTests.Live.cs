using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Announcements;
using SecureOps.Shared.Configuration;
using SkiaSharp;

namespace SecureOps.Tests.Integration.Api;

public sealed partial class AnnouncementTests
{
    [Theory]
    [InlineData("2026-09-14T01:00:37+03:00", "2026-09-13T02:00:59+03:00", "WorkEnd")]
    [InlineData("2026-09-14T01:00:37+03:00", "2026-09-14T02:00:59+03:00", null)]
    [InlineData("2026-09-30T23:59:37+03:00", "2026-10-01T00:01:59+03:00", null)]
    [InlineData("2026-09-13T01:00:37+00:00", "2026-09-13T02:00:59+03:00", "WorkEnd")]
    [InlineData("T01:00:37+03:00", "2026-09-13T02:00:59+03:00", "WorkStart")]
    public void EditingDates_PreservesStrictSaveAndExportRules(string start, string end, string? expected)
    {
        AnnouncementContent content = FinalContent() with { WorkStart = start, WorkEnd = end };
        foreach (bool complete in new[] { false, true })
        {
            string[] errors = AnnouncementValidation.Errors(content, complete);
            if (expected is null)
            { errors.Should().BeEmpty(); }
            else
            { errors.Should().ContainSingle().Which.Should().Be(expected); }
        }
        content.WorkStart.Should().Be(start);
        content.WorkEnd.Should().Be(end);
    }
    private static async Task TransientApiAsync(HttpClient admin, HttpClient denied, HttpClient anonymous, SqlConnection sql, string savedPath)
    {
        const string route = "/api/v1/announcements/preview";
        string counts = "SELECT CONCAT((SELECT COUNT(*) FROM announcements.DraftRevisions),':',(SELECT COUNT(*) FROM audit.AuditLog WHERE Action LIKE 'Announcement%'))";
        string before = (await sql.ExecuteScalarAsync<string>(counts))!;
        foreach (HttpClient client in new[] { denied, anonymous })
        { (await client.PostAsJsonAsync(route, FinalContent())).StatusCode.Should().Be(client == denied ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized); }
        using HttpResponseMessage preview = await admin.PostAsJsonAsync(route + "?id=" + Guid.NewGuid(), FinalContent() with { Subject = "", To = [] });
        preview.EnsureSuccessStatusCode();
        preview.Headers.ETag.Should().BeNull();
        preview.Headers.CacheControl!.NoStore.Should().BeTrue();
        preview.Headers.GetValues("X-Announcement-Incomplete").Single().Should().Be("Subject,To");
        preview.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("sandbox");
        (await preview.Content.ReadAsStringAsync()).Should().Contain(WebUtility.HtmlEncode(Services()[^1])).And.NotContain("<script>");
        using HttpResponseMessage saved = await admin.GetAsync(savedPath);
        string etag = saved.Headers.ETag!.Tag;
        foreach (AnnouncementContent content in new[] { FinalContent() with { Subject = "x\r\nBcc: injected" }, FinalContent() with { WorkEnd = "2026-09-12T01:00Z" }, FinalContent() with { BannerRevision = "../outside" } })
        { (await admin.PostAsJsonAsync(route, content)).StatusCode.Should().Be(HttpStatusCode.BadRequest); }
        (await sql.ExecuteScalarAsync<string>(counts)).Should().Be(before.Split(':')[0] + ":" + (int.Parse(before.Split(':')[1]) + 1)); // Explicit saved read only.
        using HttpResponseMessage current = await admin.GetAsync(savedPath);
        current.Headers.ETag!.Tag.Should().Be(etag);
    }
    [Fact]
    public async Task PreviewReceipts_RecheckBytesAndPathsWithoutRepeatingUnchangedCodecWork()
    {
        string root = Assets();
        var renderer = new AnnouncementRenderer(Options.Create(new AnnouncementOptions { AssetDirectory = root, Banners = new() { ["synthetic-v1"] = "banner.bin" } }));
        var timer = Stopwatch.StartNew();
        (byte[] Bytes, string Type, string Hash) first = await renderer.AssetAsync("synthetic-v1", TestContext.Current.CancellationToken);
        long cold = timer.ElapsedTicks;
        timer.Restart();
        for (int i = 0; i < 20; i++)
        { (await renderer.AssetAsync("synthetic-v1", TestContext.Current.CancellationToken)).Hash.Should().Be(first.Hash); }
        output.WriteLine("Local asset cold ticks={0}; 20 hash-checked reads ticks={1}; bytes={2}", cold, timer.ElapsedTicks, first.Bytes.Length);
        File.Copy(Path.Combine(Assets(SKEncodedImageFormat.Jpeg), "banner.bin"), Path.Combine(root, "banner.bin"), true);
        (await renderer.AssetAsync("synthetic-v1", TestContext.Current.CancellationToken)).Type.Should().Be("jpeg");
        await File.WriteAllBytesAsync(Path.Combine(root, "banner.bin"), new byte[64], TestContext.Current.CancellationToken);
        await FluentActions.Awaiting(() => renderer.AssetAsync("synthetic-v1", default)).Should().ThrowAsync<InvalidOperationException>();
        File.Move(Path.Combine(root, "banner.bin"), Path.Combine(root, "retained.bin"));
        await FluentActions.Awaiting(() => renderer.AssetAsync("synthetic-v1", default)).Should().ThrowAsync<FileNotFoundException>();
    }
}
