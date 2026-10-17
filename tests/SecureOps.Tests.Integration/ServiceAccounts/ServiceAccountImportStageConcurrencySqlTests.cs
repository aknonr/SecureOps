using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>
/// Import staging is serialized by an application lock (no deadlock victim, no retry of writes). Runs alone: one test holds
/// that lock, which would stall every other staging in parallel tests.
/// </summary>
[Collection(ServiceAccountWriteGateCollection.Name)]
public sealed class ServiceAccountImportStageConcurrencySqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;
    private static readonly string[] _importer = [ServiceAccountCapabilities.View, ServiceAccountCapabilities.Import];

    [ServiceAccountSqlFact]
    public async Task ParallelStagings_AreQueuedAndEachStoresItsOwnBatchWithoutDeadlock()
    {
        ServiceAccountSqlFixture fx = new();
        string orgName = "SYN ORG " + fx.Suffix;
        Guid org = await fx.OrganizationAsync(orgName);
        SynUser importer = await fx.UserAsync(_importer);
        await fx.GrantAsync(importer, ScopeKind.Organization, org);
        StageImportRequest request = new(ServiceAccountImportProfiles.CoordinationList, new DateOnly(2026, 9, 14), "Sentetik beyan: kaynak rapor tarihi test verisidir");

        for (int round = 0; round < 3; round++)
        {
            // Distinct files, so no replay: every call must stage its own batch.
            SaResult<ImportBatchView>[] results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(() =>
                fx.Service.StageImportAsync(importer.Principal, fx.Context, request, $"liste{round}-{i}.xlsx", "application/octet-stream",
                    List(orgName, $"SYN{fx.Suffix}_R{round}_{i}"), _token))));

            results.Should().OnlyContain(r => r.ErrorCode == null, "queued stagings never fail with a deadlock victim");
            results.Select(r => r.Value!.Id).Distinct().Should().HaveCount(6);
        }

        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.ImportBatches WHERE UploadedBy = @id", new { id = importer.User.Id })).Should().Be(18);
        fx.SqlDiagnostics.Should().BeEmpty();
    }

    [ServiceAccountSqlFact]
    public async Task StagingBehindAHeldStageLock_FailsAsUnavailableAndWritesNothing()
    {
        ServiceAccountSqlFixture fx = new();
        string orgName = "SYN ORG " + fx.Suffix;
        Guid org = await fx.OrganizationAsync(orgName);
        SynUser importer = await fx.UserAsync(_importer);
        await fx.GrantAsync(importer, ScopeKind.Organization, org);
        StageImportRequest request = new(ServiceAccountImportProfiles.CoordinationList, new DateOnly(2026, 9, 14), "Sentetik beyan: kaynak rapor tarihi test verisidir");

        await using SqlConnection holder = fx.Connection();
        await holder.OpenAsync(_token);
        await using SqlTransaction held = holder.BeginTransaction();
        int granted = await holder.ExecuteScalarAsync<int>(new CommandDefinition("""
            DECLARE @r int;
            EXEC @r = sp_getapplock @Resource = 'svcacct:import-stage', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0;
            SELECT @r;
            """, transaction: held, cancellationToken: _token));
        granted.Should().BeGreaterThanOrEqualTo(0);

        SaResult<ImportBatchView> blocked = await fx.Service.StageImportAsync(importer.Principal, fx.Context, request, "bekleyen.xlsx",
            "application/octet-stream", List(orgName, $"SYN{fx.Suffix}_WAIT"), _token);

        blocked.ErrorCode.Should().Be(SaErrors.Unavailable);
        fx.SqlDiagnostics.Should().ContainSingle().Which.Should().Contain("Number=51313");
        await held.RollbackAsync(_token);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.ImportBatches WHERE UploadedBy = @id", new { id = importer.User.Id })).Should().Be(0);

        // Once the lock is free the same call succeeds.
        Ok(await fx.Service.StageImportAsync(importer.Principal, fx.Context, request, "bekleyen.xlsx", "application/octet-stream",
            List(orgName, $"SYN{fx.Suffix}_WAIT"), _token)).FileName.Should().Be("bekleyen.xlsx");
    }

    private static byte[] List(string organization, string name) =>
        SyntheticWorkbook.Create([("Sheet2", [
            (1, new SynCell?[] { "Kullanıcı Adı", "Son Parola Değişiklik Zamanı", "AD veya LDAP Son Oturum Açma Zamanı", "AD Son Oturum Açma Zamanı", "Organizasyon", "Grup Direktorlugu", "Yorum" }),
            (2, new SynCell?[] { name, 45000, 45000, 45000, organization, "SYN GRUP", null })])]);

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }
}
