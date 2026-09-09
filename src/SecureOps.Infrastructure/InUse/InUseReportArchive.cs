using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

/// <summary>Private immutable version archive. One atomic envelope holds XLSX bytes and metadata.</summary>
public sealed class InUseReportArchive(IConfiguration configuration)
{
    private const int _maximumBytes = 16 * 1024 * 1024;
    private string Root()
    {
        string path = configuration["InUseReports:Directory"] ?? "";
        if (!Path.IsPathFullyQualified(path))
        { throw new InvalidOperationException("InUseReports:Directory must be an absolute private directory."); }
        path = Path.GetFullPath(path);
        foreach (string deployment in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            string relative = Path.GetRelativePath(deployment, path);
            if (relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            { throw new InvalidOperationException("Report storage must be outside deployment content."); }
        }
        CheckPath(path);
        return path;
    }
    private static void CheckPath(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            { throw new IOException("Report storage cannot traverse reparse points."); }
        }
    }
    /// <summary>Lists retained versions, without reading workbook contents or requiring configured storage.</summary>
    public IReadOnlyList<long> Versions(Guid id)
    {
        if (string.IsNullOrWhiteSpace(configuration["InUseReports:Directory"]))
        { return []; }
        string directory = Path.Combine(Root(), id.ToString("D"));
        CheckPath(directory);
        return !Directory.Exists(directory) ? [] : Directory.EnumerateFiles(directory, "*.json")
            .Select(p => long.TryParse(Path.GetFileNameWithoutExtension(p), out long version) ? version : 0)
            .Where(v => v > 0).OrderDescending().ToArray();
    }
    /// <summary>Serializes competing writers across processes; audit must succeed before committing bytes.</summary>
    public async Task<InUseReport?> AccessAsync(Guid id, long version, InUseReport? candidate,
        Func<InUseReport, Task<bool>> authorize, CancellationToken token)
    {
        if (id == Guid.Empty || version < 1)
        { throw new InvalidDataException("Invalid archive identity."); }
        string directory = Path.Combine(Root(), id.ToString("D"));
        CheckPath(directory);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, version.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".json");
        CheckPath(path);
        CheckPath(path + ".lock");
        using FileStream gate = new(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        InUseReport report;
        if (File.Exists(path))
        {
            if (new FileInfo(path).Length > _maximumBytes)
            { throw new InvalidDataException("Archive exceeds limit."); }
            using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            report = await JsonSerializer.DeserializeAsync<InUseReport>(input, cancellationToken: token)
                ?? throw new InvalidDataException("Invalid archive.");
            if (!report.Archived || report.RecordId != id || report.Version != version || report.PreparedBy == Guid.Empty
                || report.Content is null || report.Size != report.Content.LongLength
                || report.Sha256 != Convert.ToHexString(SHA256.HashData(report.Content)))
            { throw new InvalidDataException("Archive integrity check failed."); }
        }
        else
        { report = candidate ?? throw new FileNotFoundException("Requested archive is unavailable."); }
        if (!await authorize(report))
        { return null; }
        if (File.Exists(path))
        { return report; }
        report = report with { Archived = true };
        byte[] envelope = JsonSerializer.SerializeToUtf8Bytes(report);
        if (envelope.Length > _maximumBytes)
        { throw new InvalidDataException("Archive exceeds limit."); }
        // A crash may leave one bounded pending file; retries replace only that uncommitted envelope.
        string pending = path + ".pending";
        CheckPath(pending);
        using (FileStream output = new(pending, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            await output.WriteAsync(envelope, token);
            output.Flush(flushToDisk: true);
        }
        token.ThrowIfCancellationRequested();
        File.Move(pending, path, overwrite: false);
        return report;
    }
}
