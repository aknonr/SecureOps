using FluentAssertions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.Audit;

public sealed class FileAuditEventSinkTests
{
    [Fact]
    public async Task WriteBatchAsync_RetentionFloor_PreservesExistingFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "secureops-audit-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            string[] olderFiles = Enumerable.Range(1, 4)
                .Select(index => Path.Combine(directory, $"test-audit-2020010{index}.jsonl"))
                .ToArray();
            foreach (string file in olderFiles)
            {
                await File.WriteAllTextAsync(file, "{\"action\":\"SyntheticEvent\"}");
                File.SetLastWriteTimeUtc(file, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            }

            FileAuditEventSink sink = new(Options.Create(new AuditOptions
            {
                File = new AuditFileOptions { Directory = directory, FilePrefix = "test-audit", RetainedFileCountLimit = 1 }
            }));
            await sink.WriteBatchAsync([new AuditEvent { Actor = "synthetic", Action = "SyntheticEvent" }], default);

            Directory.GetFiles(directory).Should().HaveCount(5);
            foreach (string file in olderFiles)
            {
                (await File.ReadAllTextAsync(file)).Should().Be("{\"action\":\"SyntheticEvent\"}");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WriteBatchAsync_WritesJsonLinesToConfiguredDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "secureops-audit-tests", Guid.NewGuid().ToString("N"));
        try
        {
            FileAuditEventSink sink = new(Options.Create(new AuditOptions
            {
                File = new AuditFileOptions
                {
                    Directory = directory,
                    FilePrefix = "test-audit",
                    MaxFileSizeMB = 10,
                    RetainedFileCountLimit = 10
                }
            }));

            await sink.WriteBatchAsync(
                [
                    new AuditEvent
                    {
                        Actor = "CONTOSO\\lead.user",
                        Action = "IdentityLookupSucceeded",
                        CorrelationId = "trace-file-test",
                        Details = new
                        {
                            normalizedAccount = "pam12356",
                            matchedAccount = "pam12356",
                            resultStatus = "Succeeded"
                        }
                    }
                ],
                CancellationToken.None);

            string file = Directory.GetFiles(directory, "test-audit-*.jsonl").Should().ContainSingle().Subject;
            string content = await File.ReadAllTextAsync(file);

            content.Should().Contain("IdentityLookupSucceeded");
            content.Should().Contain("pam12356");
            content.Should().NotContain("displayName");
            content.Should().NotContain("mail");
            content.Should().NotContain("department");
            content.Should().NotContain("manager");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
