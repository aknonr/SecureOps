using FluentAssertions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.Audit;

public sealed class FileAuditEventSinkTests
{
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
            string content = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);

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
