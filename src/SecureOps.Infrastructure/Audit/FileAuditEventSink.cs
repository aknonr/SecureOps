using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Audit;

/// <summary>
/// JSONL file audit sink for development and test environments.
/// </summary>
public sealed class FileAuditEventSink : IAuditEventSink
{
    private readonly AuditOptions _options;
    private readonly long _maxFileSizeBytes;

    /// <summary>
    /// Initializes a file audit sink.
    /// </summary>
    /// <param name="options">Audit options.</param>
    public FileAuditEventSink(IOptions<AuditOptions> options)
    {
        _options = options.Value;
        _maxFileSizeBytes = _options.File.MaxFileSizeMB * 1024L * 1024L;
        Directory.CreateDirectory(_options.File.Directory);
    }

    /// <inheritdoc />
    public async Task WriteBatchAsync(IReadOnlyCollection<AuditEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        string path = GetWritablePath();
        string[] lines = events
            .Select(auditEvent => JsonSerializer.Serialize(auditEvent, AuditJson.SerializerOptions))
            .ToArray();

        await File.AppendAllLinesAsync(path, lines, Encoding.UTF8, cancellationToken);
        EnforceRetention();
    }

    private string GetWritablePath()
    {
        string date = DateTimeOffset.UtcNow.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        string prefix = string.IsNullOrWhiteSpace(_options.File.FilePrefix)
            ? "secureops-audit"
            : _options.File.FilePrefix;

        int sequence = 0;
        while (true)
        {
            string suffix = sequence == 0 ? string.Empty : $"-{sequence:000}";
            string path = Path.Combine(_options.File.Directory, $"{prefix}-{date}{suffix}.jsonl");
            if (!File.Exists(path) || new FileInfo(path).Length < _maxFileSizeBytes)
            {
                return path;
            }

            sequence++;
        }
    }

    private void EnforceRetention()
    {
        DirectoryInfo directory = new(_options.File.Directory);
        string prefix = string.IsNullOrWhiteSpace(_options.File.FilePrefix)
            ? "secureops-audit"
            : _options.File.FilePrefix;

        foreach (FileInfo file in directory
                     .GetFiles($"{prefix}-*.jsonl")
                     .OrderByDescending(file => file.LastWriteTimeUtc)
                     .Skip(_options.File.RetainedFileCountLimit))
        {
            file.Delete();
        }
    }
}
