using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Identity;

namespace SecureOps.Tests.Unit.Identity;

public sealed class IdentityLookupServiceTests
{
    [Fact]
    public async Task LookupAsync_WhenUserFound_ReturnsApprovedFieldsAndAudits()
    {
        InMemoryAuditWriter audit = new();
        IdentityLookupService service = CreateService(
            new MockIdentityDirectoryProvider(new[]
            {
                new DirectoryUserRecord(
                    "Example Admin",
                    "pam12356",
                    "pam12356@contoso.local",
                    "example.admin@contoso.local",
                    "Windows Operations",
                    "Systems Engineer",
                    "Example Manager",
                    true,
                    false,
                    "Mock")
            }),
            audit);

        IdentityLookupResult result = await service.LookupAsync(CreateRequest(), CreateContext(), CancellationToken.None);

        result.Status.Should().Be(IdentityLookupResultStatus.Found);
        result.Response!.User!.SamAccountName.Should().Be("pam12356");
        result.Response.User.Department.Should().Be("Windows Operations");
        result.Response.User.Locked.Should().BeFalse();
        audit.Events.Select(x => x.Action).Should().Contain(new[]
        {
            AuditActions.IdentityLookupRequested,
            AuditActions.IdentityLookupSucceeded
        });
    }

    [Fact]
    public async Task LookupAsync_WhenUserDisabledAndLocked_ReturnsStateFields()
    {
        InMemoryAuditWriter audit = new();
        IdentityLookupService service = CreateService(
            new MockIdentityDirectoryProvider(new[]
            {
                new DirectoryUserRecord(
                    "Disabled Admin",
                    "pam12356",
                    null,
                    null,
                    null,
                    null,
                    null,
                    false,
                    true,
                    "Mock")
            }),
            audit);

        IdentityLookupResult result = await service.LookupAsync(CreateRequest(), CreateContext(), CancellationToken.None);

        result.Response!.User!.Enabled.Should().BeFalse();
        result.Response.User.Locked.Should().BeTrue();
    }

    [Fact]
    public async Task LookupAsync_WhenUserNotFound_ReturnsNotFoundAndAudits()
    {
        InMemoryAuditWriter audit = new();
        IdentityLookupService service = CreateService(new MockIdentityDirectoryProvider(Array.Empty<DirectoryUserRecord>()), audit);

        IdentityLookupResult result = await service.LookupAsync(CreateRequest(), CreateContext(), CancellationToken.None);

        result.Status.Should().Be(IdentityLookupResultStatus.NotFound);
        result.Response!.Status.Should().Be("NotFound");
        audit.Events.Select(x => x.Action).Should().Contain(AuditActions.IdentityLookupNotFound);
    }

    [Fact]
    public async Task LookupAsync_WhenProviderFails_ReturnsFailureAndAudits()
    {
        InMemoryAuditWriter audit = new();
        IdentityLookupService service = CreateService(new ThrowingDirectoryProvider(), audit);

        IdentityLookupResult result = await service.LookupAsync(CreateRequest(), CreateContext(), CancellationToken.None);

        result.Status.Should().Be(IdentityLookupResultStatus.Failed);
        result.ErrorCode.Should().Be("ProviderUnavailable");
        audit.Events.Select(x => x.Action).Should().Contain(AuditActions.IdentityLookupFailed);
    }

    [Fact]
    public async Task LookupAsync_WhenInputInvalid_ReturnsInvalidAndAuditsFailure()
    {
        InMemoryAuditWriter audit = new();
        IdentityLookupService service = CreateService(new MockIdentityDirectoryProvider(), audit);

        IdentityLookupResult result = await service.LookupAsync(
            CreateRequest(account: "pam*"),
            CreateContext(),
            CancellationToken.None);

        result.Status.Should().Be(IdentityLookupResultStatus.Invalid);
        audit.Events.Select(x => x.Action).Should().Contain(AuditActions.IdentityLookupFailed);
    }

    private static IdentityLookupService CreateService(
        IIdentityDirectoryProvider provider,
        InMemoryAuditWriter audit)
    {
        return new IdentityLookupService(
            new IdentityAccountNormalizer(Options.Create(new IdentityLookupOptions())),
            new MockPamAccountResolver(),
            provider,
            audit,
            NullLogger<IdentityLookupService>.Instance);
    }

    private static IdentityLookupRequest CreateRequest(string account = "pam12356")
    {
        return new IdentityLookupRequest(account, "EVT-54321 incident response verification", null, "EVT-54321");
    }

    private static IdentityLookupExecutionContext CreateContext()
    {
        return new IdentityLookupExecutionContext("CONTOSO\\lead.user", "10.4.22.18", "trace-1");
    }

    private sealed class ThrowingDirectoryProvider : IIdentityDirectoryProvider
    {
        public Task<DirectoryUserRecord?> FindUserAsync(string normalizedAccount, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Directory unavailable.");
        }
    }
}
