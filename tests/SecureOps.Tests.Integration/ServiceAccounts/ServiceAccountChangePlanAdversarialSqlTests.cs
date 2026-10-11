using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;
using Xunit.Abstractions;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>Additional T1-T8 attacks against E/db56856. Only the disposable harness database is allowed.</summary>
[Collection(ServiceAccountWriteGateCollection.Name)]
public sealed class ServiceAccountChangePlanAdversarialSqlTests(ITestOutputHelper output)
{
    private static readonly CancellationToken _token = CancellationToken.None;

    [ServiceAccountSqlFact]
    public async Task ApproveAndCancel_SynchronizedAcrossServices_32Rounds_OneDurableTransition()
    {
        Setup s = await SetupAsync();
        using SqlErrorProbe errors = new(s.Fx.Connection().Database);
        ServiceAccountService otherService = s.Fx.WithDirectory(null);
        int approvals = 0, cancellations = 0;
        for (int round = 0; round < 32; round++)
        {
            ChangePlanView plan = await PreviewAsync(s, await DraftAsync(s, await AccountAsync(s, "Race" + round)));
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var approveReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<SaResult<ChangePlanView>> Approve()
            {
                approveReady.SetResult();
                await release.Task;
                return await otherService.ApproveChangePlanAsync(s.Verifier.Principal, s.Fx.Context, plan.Id, Approval(plan), _token);
            }

            async Task<SaResult<ChangePlanView>> Cancel()
            {
                cancelReady.SetResult();
                await release.Task;
                return await s.Fx.Service.CancelChangePlanAsync(s.Planner.Principal, s.Fx.Context, plan.Id,
                    new CancelChangePlanRequest(plan.Version, "Synthetic race cancellation"), _token);
            }

            Task<SaResult<ChangePlanView>> approve = Approve(), cancel = Cancel();
            await Task.WhenAll(approveReady.Task, cancelReady.Task);
            release.SetResult();
            SaResult<ChangePlanView>[] results = await Task.WhenAll(approve, cancel);
            s.Fx.SqlDiagnostics.Should().BeEmpty($"round {round}: no persistence error or deadlock, including 1205");
            errors.Numbers.Should().NotContain(1205, "SqlClient also reports errors hidden by read retries");
            results.Count(r => r.IsSuccess).Should().Be(1, $"round {round}");
            SaResult<ChangePlanView> loser = results.Single(r => !r.IsSuccess);
            loser.ErrorCode.Should().BeOneOf(SaErrors.Conflict, SaErrors.ChangePlanState);
            ChangePlanView final = Ok(await s.Fx.Service.ChangePlanAsync(s.Planner.Principal, s.Fx.Context, plan.Id, _token));
            final.Status.Should().Be(approve.Result.IsSuccess ? "Approved" : "Cancelled");
            final.Events.Select(e => e.Event).Should().Equal("Created", "Previewed", final.Status);
            final.Events[^1].ToStatus.Should().Be(final.Status);
            loser.Current.Should().BeOfType<ChangePlanView>().Which.Status.Should().Be(final.Status);
            await using SqlConnection connection = s.Fx.Connection();
            (int Approvals, int History, int Audit) counts = await connection.QuerySingleAsync<(int Approvals, int History, int Audit)>("""
                SELECT (SELECT COUNT(*) FROM svcacct.ChangePlanApprovals WHERE PlanId = @id),
                    (SELECT COUNT(*) FROM svcacct.History WHERE EntityType = 'ChangePlan' AND EntityId = @id),
                    (SELECT COUNT(*) FROM audit.AuditLog WHERE DetailsJson LIKE @pattern);
                """, new { id = plan.Id, pattern = "%" + plan.Id.ToString("D") + "%" });
            counts.Should().Be((approve.Result.IsSuccess ? 1 : 0, 3, 3), $"round {round}: loser leaves no durable writes");
            if (approve.Result.IsSuccess)
            {
                approvals++;
            }
            else
            {
                cancellations++;
            }

            string beforeReplay = await SnapshotAsync(s, plan.Id);
            (await ApproveAsync(s, s.Verifier, plan, Approval(plan))).ErrorCode.Should().Be(SaErrors.ChangePlanState);
            (await s.Fx.Service.CancelChangePlanAsync(s.Planner.Principal, s.Fx.Context, plan.Id,
                new CancelChangePlanRequest(plan.Version, "Synthetic replay"), _token)).IsSuccess.Should().BeFalse();
            (await SnapshotAsync(s, plan.Id)).Should().Be(beforeReplay);
        }

        output.WriteLine($"32 synchronized rounds: approval winners={approvals}, cancellation winners={cancellations}; no 1205.");
    }

    [ServiceAccountSqlFact]
    public async Task StaleApproval_AfterRemovalAndIdenticalDigestRepreview_WritesNothing()
    {
        Setup s = await SetupAsync();
        AccountDetail first = await AccountAsync(s, "StaleA"), removed = await AccountAsync(s, "StaleB");
        ChangePlanView old = await PreviewAsync(s, await DraftAsync(s, first, removed));
        ApproveChangePlanRequest captured = Approval(old);
        ChangePlanView changed = Ok(await s.Fx.Service.UpdateChangePlanAsync(s.Planner.Principal, s.Fx.Context, old.Id,
            new UpdateChangePlanRequest(old.Version, [Input(first)]), _token));
        string before = await SnapshotAsync(s, old.Id);
        (await ApproveAsync(s, s.Verifier, old, captured)).Field.Should().Be("planNotPreviewed");
        (await SnapshotAsync(s, old.Id)).Should().Be(before);
        ChangePlanView current = await PreviewAsync(s, changed);
        ChangePlanView sameContent = await PreviewAsync(s, current);
        sameContent.Preview!.Sha256.Should().Be(current.Preview!.Sha256);
        sameContent.Preview.Version.Should().BeGreaterThan(current.Preview.Version);
        before = await SnapshotAsync(s, old.Id);
        foreach (ApproveChangePlanRequest attack in new[] { captured, Approval(current), Approval(sameContent) with { Sha256 = old.Preview!.Sha256 } })
        {
            SaResult<ChangePlanView> refused = await ApproveAsync(s, s.Verifier, sameContent, attack);
            (refused.ErrorCode, refused.Field).Should().Be((SaErrors.ChangePlanState, "previewStale"));
            (await SnapshotAsync(s, old.Id)).Should().Be(before);
        }

        Ok(await ApproveAsync(s, s.Verifier, sameContent)).Status.Should().Be("Approved");
        before = await SnapshotAsync(s, old.Id);
        foreach (SynUser caller in new[] { s.Verifier, s.Other })
        {
            (await ApproveAsync(s, caller, sameContent)).Field.Should().Be("planNotPreviewed");
            (await SnapshotAsync(s, old.Id)).Should().Be(before);
        }
    }

    [ServiceAccountSqlFact]
    public async Task EditorHistory_AfterUndoAndRepreview_ServiceAndSqlStillRefuseApproval()
    {
        Setup s = await SetupAsync();
        AccountDetail account = await AccountAsync(s, "Editor");
        ChangePlanView plan = await DraftAsync(s, account);
        plan = Ok(await s.Fx.Service.UpdateChangePlanAsync(s.Verifier.Principal, s.Fx.Context, plan.Id,
            new UpdateChangePlanRequest(plan.Version, [Input(account) with { TargetGmsaName = "gmsaEdited" }]), _token));
        plan = Ok(await s.Fx.Service.UpdateChangePlanAsync(s.Planner.Principal, s.Fx.Context, plan.Id,
            new UpdateChangePlanRequest(plan.Version, [Input(account)]), _token));
        plan = await PreviewAsync(s, plan);
        string before = await SnapshotAsync(s, plan.Id);
        foreach ((SynUser actor, string field) in new[] { (s.Planner, "approverIsPlanner"), (s.Verifier, "approverChangedPlan") })
        {
            SaResult<ChangePlanView> refused = await ApproveAsync(s, actor, plan);
            (refused.ErrorCode, refused.Field).Should().Be((SaErrors.Forbidden, field));
            await DirectApprovalRefusedAsync(s, plan.Id, plan.Id, plan.Preview!.Version, actor, 51393);
            (await SnapshotAsync(s, plan.Id)).Should().Be(before);
        }

        Ok(await ApproveAsync(s, s.Other, plan)).Status.Should().Be("Approved");
    }

    [ServiceAccountSqlFact]
    public async Task ScopeShrinksToOneOfTwoAccounts_AllPlanOperationsMatchMissing_AndWriteNothing()
    {
        Setup s = await SetupAsync();
        Guid otherOrg = await s.Fx.OrganizationAsync("SYN SECOND " + s.Fx.Suffix);
        foreach (SynUser actor in new[] { s.Planner, s.Verifier, s.Other })
        {
            await s.Fx.GrantAsync(actor, ScopeKind.Organization, otherOrg);
        }

        AccountDetail first = await AccountAsync(s, "ScopeA"), second = await AccountAsync(s, "ScopeB", otherOrg);
        ChangePlanView plan = await PreviewAsync(s, await DraftAsync(s, first, second));
        await using (SqlConnection connection = s.Fx.Connection())
        {
            await connection.ExecuteAsync("""
                UPDATE svcacct.ScopeGrants SET RevokedAt = SYSUTCDATETIME(), RevokedBy = @by, RevokeReason = N'Synthetic partial revocation'
                WHERE OrganizationId = @org AND UserId IN @users AND RevokedAt IS NULL;
                """, new { by = s.Other.User.Id, org = otherOrg, users = new[] { s.Planner.User.Id, s.Verifier.User.Id } });
        }

        string before = await SnapshotAsync(s, plan.Id);
        foreach (SynUser actor in new[] { s.Planner, s.Verifier })
        {
            Ok(await s.Fx.Service.AccountAsync(actor.Principal, s.Fx.Context, first.Summary.Id, _token));
            foreach (Guid id in new[] { plan.Id, Guid.NewGuid() })
            {
                Hidden(await s.Fx.Service.ChangePlanAsync(actor.Principal, s.Fx.Context, id, _token));
                Hidden(await s.Fx.Service.ChangePlanItemsAsync(actor.Principal, s.Fx.Context, id, 1, 200, _token));
                Hidden(await s.Fx.Service.PreviewChangePlanAsync(actor.Principal, s.Fx.Context, id, new PreviewChangePlanRequest(plan.Version), _token));
                Hidden(await s.Fx.Service.UpdateChangePlanAsync(actor.Principal, s.Fx.Context, id, new UpdateChangePlanRequest(plan.Version, [Input(first)]), _token));
                Hidden(await s.Fx.Service.ApproveChangePlanAsync(actor.Principal, s.Fx.Context, id, Approval(plan), _token));
                Hidden(await s.Fx.Service.CancelChangePlanAsync(actor.Principal, s.Fx.Context, id, new CancelChangePlanRequest(plan.Version, "Synthetic"), _token));
            }

            foreach (Guid filter in new[] { first.Summary.Id, second.Summary.Id })
            {
                Ok(await s.Fx.Service.ChangePlansAsync(actor.Principal, s.Fx.Context, new ChangePlanListQuery(AccountId: filter), _token)).Items.Should().BeEmpty();
            }
        }

        (await SnapshotAsync(s, plan.Id)).Should().Be(before);
        Ok(await ApproveAsync(s, s.Other, plan)).Status.Should().Be("Approved", "a still-authorized independent verifier is the positive control");
    }

    [ServiceAccountSqlFact]
    public async Task Patch_ForeignAccountAndForeignRequest_RefuseAtomicallyWithoutLeakingHiddenName()
    {
        Setup s = await SetupAsync();
        AccountDetail first = await AccountAsync(s, "PatchA"), other = await AccountAsync(s, "PatchB");
        AccountDetail withRequest = Ok(await s.Fx.Service.CreateRequestAsync(s.Planner.Principal, s.Fx.Context, other.Summary.Id,
            new CreateWorkRequest("GmsaConversion"), _token));
        ChangePlanView plan = await PreviewAsync(s, await DraftAsync(s, first));
        string before = await SnapshotAsync(s, plan.Id);
        SaResult<ChangePlanView> requestAttack = await s.Fx.Service.UpdateChangePlanAsync(s.Planner.Principal, s.Fx.Context, plan.Id,
            new UpdateChangePlanRequest(plan.Version, [Input(first) with { RequestId = withRequest.Requests.Single().Id }]), _token);
        requestAttack.ErrorCode.Should().Be(SaErrors.ChangePlanAccountsRefused);
        ((IReadOnlyList<ChangePlanAccountResult>)requestAttack.Current!).Single().Outcome.Should().Be("InvalidRequest");
        SynUser outsider = await s.Fx.UserAsync([.. ServiceAccountCapabilities.All]);
        Guid outsideOrg = await s.Fx.OrganizationAsync("SYN HIDDEN " + s.Fx.Suffix);
        await s.Fx.GrantAsync(outsider, ScopeKind.Organization, outsideOrg);
        AccountDetail hidden = Ok(await s.Fx.Service.CreateAccountAsync(outsider.Principal, s.Fx.Context,
            new CreateAccountRequest("SYN_HIDDEN_" + s.Fx.Suffix, "SYN", outsideOrg, "Synthetic hidden account"), _token));
        foreach (Guid injected in new[] { hidden.Summary.Id, Guid.NewGuid() })
        {
            SaResult<ChangePlanView> refused = await s.Fx.Service.UpdateChangePlanAsync(s.Planner.Principal, s.Fx.Context, plan.Id,
                new UpdateChangePlanRequest(plan.Version, [Input(first), new ChangePlanAccountInput(injected, "gmsaInjected")]), _token);
            refused.ErrorCode.Should().Be(SaErrors.ChangePlanAccountsRefused);
            ChangePlanAccountResult result = ((IReadOnlyList<ChangePlanAccountResult>)refused.Current!)[1];
            (result.Outcome, result.AccountName).Should().Be(("Unavailable", (string?)null));
            JsonSerializer.Serialize(refused).Should().NotContain(hidden.Summary.AccountName);
        }

        (await SnapshotAsync(s, plan.Id)).Should().Be(before);
    }

    [ServiceAccountSqlFact]
    public async Task ForeignPreviewAndItemIds_CannotSelectOtherPlanOrApproveItsDigest()
    {
        Setup s = await SetupAsync();
        ChangePlanView first = await PreviewAsync(s, await DraftAsync(s, await AccountAsync(s, "IdorA")));
        ChangePlanView second = await PreviewAsync(s, await DraftAsync(s, await AccountAsync(s, "IdorB")));
        ChangePlanItemPage firstItems = Ok(await s.Fx.Service.ChangePlanItemsAsync(s.Verifier.Principal, s.Fx.Context, first.Id, 1, 200, _token));
        ChangePlanItemPage secondItems = Ok(await s.Fx.Service.ChangePlanItemsAsync(s.Verifier.Principal, s.Fx.Context, second.Id, 1, 200, _token));
        firstItems.Items.Select(i => i.Id).Should().NotIntersectWith(secondItems.Items.Select(i => i.Id));
        string beforeFirst = await SnapshotAsync(s, first.Id), beforeSecond = await SnapshotAsync(s, second.Id);
        SaResult<ChangePlanView> swapped = await ApproveAsync(s, s.Verifier, first, Approval(second));
        (swapped.ErrorCode, swapped.Field).Should().Be((SaErrors.ChangePlanState, "previewStale"));
        await DirectApprovalRefusedAsync(s, first.Id, second.Id, second.Preview!.Version, s.Verifier, 51393);
        await using SqlConnection connection = s.Fx.Connection();
        Guid foreignPreview = await connection.ExecuteScalarAsync<Guid>("SELECT Id FROM svcacct.ChangePlanPreviews WHERE PlanId = @id AND Version = 1", new { id = second.Id });
        foreach (Guid guessedPlan in new[] { foreignPreview, secondItems.Items.Single().Id })
        {
            Hidden(await s.Fx.Service.ChangePlanAsync(s.Verifier.Principal, s.Fx.Context, guessedPlan, _token));
            Hidden(await s.Fx.Service.ChangePlanItemsAsync(s.Verifier.Principal, s.Fx.Context, guessedPlan, 1, 200, _token));
        }

        (await SnapshotAsync(s, first.Id)).Should().Be(beforeFirst);
        (await SnapshotAsync(s, second.Id)).Should().Be(beforeSecond);
    }

    [ServiceAccountSqlFact]
    public async Task AppendedPreviewItem_WithoutUpdatingDigest_ApprovalFailsClosed()
    {
        Setup s = await SetupAsync();
        ChangePlanView plan = await PreviewAsync(s, await DraftAsync(s, await AccountAsync(s, "Digest")));
        await using SqlConnection connection = s.Fx.Connection();
        await connection.ExecuteAsync("""
            INSERT INTO svcacct.ChangePlanItems(Id, PreviewId, AccountId, TargetIdentity, Flag)
            SELECT NEWID(), v.Id, a.AccountId, N'gmsaInjected', 'NoScan'
            FROM svcacct.ChangePlanPreviews v JOIN svcacct.ChangePlanAccounts a ON a.PlanId = v.PlanId WHERE v.PlanId = @id;
            """, new { id = plan.Id });
        string before = await SnapshotAsync(s, plan.Id);
        SaResult<ChangePlanView> refused = await ApproveAsync(s, s.Verifier, plan);
        (refused.ErrorCode, refused.Field).Should().Be((SaErrors.ChangePlanState, "previewStale"));
        (await SnapshotAsync(s, plan.Id)).Should().Be(before);
        s.Fx.SqlDiagnostics.Should().BeEmpty();
    }

    [ServiceAccountSqlFact]
    public async Task AppendOnlyTables_PopulatedRows_RejectEveryUpdateAndDelete_AsOwnerAndRuntime()
    {
        Setup s = await SetupAsync();
        using SqlErrorProbe errors = new(s.Fx.Connection().Database);
        ChangePlanView plan = await PreviewAsync(s, await DraftAsync(s, await AccountAsync(s, "Guards")));
        Ok(await ApproveAsync(s, s.Verifier, plan));
        await using SqlConnection connection = s.Fx.Connection();
        await connection.OpenAsync();
        // Unreferenced rows let DELETE reach each append-only trigger instead of failing first on a foreign key.
        await connection.ExecuteAsync("""
            INSERT INTO svcacct.ChangePlanPreviews(Id, PlanId, Version, Sha256, ScanFreshDays, ItemCount, CreatedBy, CreatedAt)
            VALUES(NEWID(), @id, 999, REPLICATE('a',64), 7, 1, @actor, SYSUTCDATETIME());
            INSERT INTO svcacct.ChangeItemChecks(ItemId, State, OutsideWindow, CheckedBy, CheckedAt)
            SELECT i.Id, 'Done', 0, @actor, SYSUTCDATETIME() FROM svcacct.ChangePlanItems i
            JOIN svcacct.ChangePlanPreviews v ON v.Id = i.PreviewId WHERE v.PlanId = @id;
            INSERT INTO svcacct.ChangePlanItems(Id, PreviewId, AccountId, ComponentName, TargetIdentity, Flag)
            SELECT NEWID(), v.Id, a.AccountId, N'SYN_GUARD_UNREFERENCED', N'gmsaSynAttack', 'NoScan'
            FROM svcacct.ChangePlanPreviews v JOIN svcacct.ChangePlanAccounts a ON a.PlanId = v.PlanId
            WHERE v.PlanId = @id AND v.Version = 1;
            """, new { id = plan.Id, actor = s.Planner.User.Id });
        (string Table, string Set, string Where)[] targets =
        [
            ("ChangePlanAccounts", "TargetGmsaName = N'gmsaTampered'", "PlanId = @id"),
            ("ChangePlanPreviews", "Sha256 = REPLICATE('f',64)", "PlanId = @id AND Version = 999"),
            ("ChangePlanItems", "TargetIdentity = N'gmsaTampered'", "ComponentName = N'SYN_GUARD_UNREFERENCED' AND PreviewId IN (SELECT Id FROM svcacct.ChangePlanPreviews WHERE PlanId = @id)"),
            ("ChangePlanApprovals", "Reason = N'Tampered'", "PlanId = @id"),
            ("ChangeItemChecks", "State = 'Failed'", "ItemId IN (SELECT i.Id FROM svcacct.ChangePlanItems i JOIN svcacct.ChangePlanPreviews v ON v.Id = i.PreviewId WHERE v.PlanId = @id)"),
            ("ChangePlanEvents", "Actor = @actor", "PlanId = @id")
        ];
        string before = await SnapshotAsync(s, plan.Id);
        foreach ((string table, string set, string where) in targets)
        {
            (await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM svcacct.{table} WHERE {where}", new { id = plan.Id })).Should().BePositive();
            foreach (string sql in new[] { $"UPDATE svcacct.{table} SET {set} WHERE {where}", $"DELETE FROM svcacct.{table} WHERE {where}" })
            {
                await MutationRefusedAsync(connection, sql, new { id = plan.Id, actor = s.Other.User.Id }, false, s.Fx.Suffix, 51392);
                await MutationRefusedAsync(connection, sql, new { id = plan.Id, actor = s.Other.User.Id }, true, s.Fx.Suffix, 229);
                (await SnapshotAsync(s, plan.Id)).Should().Be(before, $"{table}: denied mutation is atomic");
            }
        }

        errors.Numbers.Should().Contain(51392).And.Contain(229).And.NotContain(1205, "the command-error probe is active before repository retries");
        output.WriteLine("24 denied populated-row attacks: six tables x UPDATE/DELETE x owner/runtime; rows unchanged; SqlClient probe observed 51392 and 229.");
    }

    [ServiceAccountSqlFact]
    [Trait("Category", "SecurityRegression")]
    public Task FixedTitle_RuntimeCaseOnlyUpdate_MustBeRejected() => FixedTitleAttackAsync("case");

    [ServiceAccountSqlFact]
    [Trait("Category", "SecurityRegression")]
    public Task FixedTitle_RuntimeTrailingSpaceUpdate_MustBeRejected() => FixedTitleAttackAsync("space");

    private async Task FixedTitleAttackAsync(string attack)
    {
        Setup s = await SetupAsync();
        ChangePlanView plan = await DraftAsync(s, await AccountAsync(s, "Fixed"));
        await using SqlConnection connection = s.Fx.Connection();
        await connection.OpenAsync();
        string replacement = attack == "case" ? plan.Title.ToLowerInvariant() : plan.Title + " ";
        string before = await SnapshotAsync(s, plan.Id);
        await MutationRefusedAsync(connection, "UPDATE svcacct.ChangePlans SET Title = @replacement WHERE Id = @id",
            new { id = plan.Id, replacement }, true, s.Fx.Suffix, 51394, async transaction =>
            {
                string? stored = await connection.ExecuteScalarAsync<string>("SELECT Title FROM svcacct.ChangePlans WHERE Id = @id",
                    new { id = plan.Id }, transaction);
                stored.Should().Be(replacement);
                output.WriteLine($"Runtime role accepted {attack} title mutation: before={JsonSerializer.Serialize(plan.Title)} after={JsonSerializer.Serialize(stored)}; probe rolled back.");
            });
        (await SnapshotAsync(s, plan.Id)).Should().Be(before);
    }

    private static async Task MutationRefusedAsync(SqlConnection connection, string sql, object parameters, bool runtime, string suffix, int number,
        Func<SqlTransaction, Task>? accepted = null)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        bool impersonated = false;
        try
        {
            if (runtime)
            {
                string user = "sa_attack_" + suffix;
                await connection.ExecuteAsync($"CREATE USER [{user}] WITHOUT LOGIN; ALTER ROLE svcacct_api_runtime ADD MEMBER [{user}]; EXECUTE AS USER = '{user}';",
                    transaction: transaction);
                impersonated = true;
            }

            Func<Task> mutate = async () =>
            {
                (await connection.ExecuteAsync(sql, parameters, transaction)).Should().BePositive("a bypass probe must actually match a row");
                if (accepted is not null)
                {
                    await accepted(transaction);
                }
            };
            (await mutate.Should().ThrowAsync<SqlException>("the database must reject the mutation")).Which.Number.Should().Be(number);
        }
        finally
        {
            if (impersonated)
            {
                await connection.ExecuteAsync("REVERT;", transaction: transaction);
            }

            await transaction.RollbackAsync();
        }
    }

    private static async Task DirectApprovalRefusedAsync(Setup s, Guid planId, Guid previewPlanId, int version, SynUser actor, int number)
    {
        await using SqlConnection connection = s.Fx.Connection();
        await connection.OpenAsync();
        await MutationRefusedAsync(connection, """
            INSERT INTO svcacct.ChangePlanApprovals(Id, PlanId, PreviewId, Sha256, OcoNumber, WindowStart, WindowEnd, Reason, ApprovedBy, ApprovedAt)
            SELECT NEWID(), @planId, Id, Sha256, N'OCO-SYN-ATTACK', SYSUTCDATETIME(), DATEADD(hour, 1, SYSUTCDATETIME()),
                N'Synthetic direct approval attack', @actor, SYSUTCDATETIME()
            FROM svcacct.ChangePlanPreviews WHERE PlanId = @previewPlanId AND Version = @version;
            """, new { planId, previewPlanId, version, actor = actor.User.Id }, true, s.Fx.Suffix, number);
    }

    private sealed record Setup(ServiceAccountSqlFixture Fx, SynUser Planner, SynUser Verifier, SynUser Other, Guid Org);

    // Capture command errors before repository read retries can hide a deadlock from the service logger.
    private sealed class SqlErrorProbe : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly string _database;
        private readonly IDisposable _all;
        private readonly List<IDisposable> _listeners = [];
        public ConcurrentQueue<int> Numbers { get; } = new();

        public SqlErrorProbe(string database)
        {
            _database = database;
            _all = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "SqlClientDiagnosticListener")
            {
                _listeners.Add(listener.Subscribe(this, name => name.EndsWith("WriteCommandError", StringComparison.Ordinal)));
            }
        }

        public void OnNext(KeyValuePair<string, object?> entry)
        {
            object? payload = entry.Value;
            if (payload?.GetType().GetProperty("Command")?.GetValue(payload) is SqlCommand command
                && command.Connection?.Database == _database
                && payload.GetType().GetProperty("Exception")?.GetValue(payload) is SqlException error)
            {
                Numbers.Enqueue(error.Number);
            }
        }

        public void OnError(Exception error) { }
        public void OnCompleted() { }

        public void Dispose()
        {
            _all.Dispose();
            foreach (IDisposable listener in _listeners)
            {
                listener.Dispose();
            }
        }
    }

    private static async Task<Setup> SetupAsync()
    {
        SqlConnectionStringBuilder guard = new(Environment.GetEnvironmentVariable(ServiceAccountSqlFactAttribute.Variable));
        guard.DataSource.Should().Be("(localdb)\\SecureOpsResourcesV1");
        guard.InitialCatalog.Should().StartWith("SecureOps_Sa");
        guard.IntegratedSecurity.Should().BeTrue();
        Environment.GetEnvironmentVariable(ServiceAccountSqlFixture.RuntimeVariable).Should().BeNullOrEmpty("this runner impersonates its own local runtime user");
        ServiceAccountSqlFixture fx = new();
        Guid org = await fx.OrganizationAsync("SYN ATTACK " + fx.Suffix);
        SynUser planner = await fx.UserAsync([.. ServiceAccountCapabilities.All]);
        SynUser verifier = await fx.UserAsync([.. ServiceAccountCapabilities.All]);
        SynUser other = await fx.UserAsync([.. ServiceAccountCapabilities.All]);
        foreach (SynUser actor in new[] { planner, verifier, other })
        {
            await fx.GrantAsync(actor, ScopeKind.Organization, org);
        }

        return new Setup(fx, planner, verifier, other, org);
    }

    private static async Task<AccountDetail> AccountAsync(Setup s, string label, Guid? organization = null) =>
        Ok(await s.Fx.Service.CreateAccountAsync(s.Planner.Principal, s.Fx.Context,
            new CreateAccountRequest($"SYN_ATTACK_{label}_{s.Fx.Suffix}", "SYN", organization ?? s.Org, "Synthetic adversarial fixture"), _token));

    private static ChangePlanAccountInput Input(AccountDetail account) => new(account.Summary.Id, "gmsaSynAttack");

    private static async Task<ChangePlanView> DraftAsync(Setup s, params AccountDetail[] accounts) =>
        Ok(await s.Fx.Service.CreateChangePlanAsync(s.Planner.Principal, s.Fx.Context,
            new CreateChangePlanRequest("SYN IMMUTABLE PLAN " + s.Fx.Suffix, [.. accounts.Select(Input)]), _token));

    private static async Task<ChangePlanView> PreviewAsync(Setup s, ChangePlanView plan) =>
        Ok(await s.Fx.Service.PreviewChangePlanAsync(s.Planner.Principal, s.Fx.Context, plan.Id, new PreviewChangePlanRequest(plan.Version), _token));

    private static ApproveChangePlanRequest Approval(ChangePlanView plan)
    {
        DateTimeOffset start = DateTimeOffset.UtcNow.AddHours(1);
        return new ApproveChangePlanRequest(plan.Preview!.Version, plan.Preview.Sha256, "OCO-SYN-ATTACK", start, start.AddHours(2), "Synthetic approval");
    }

    private static Task<SaResult<ChangePlanView>> ApproveAsync(Setup s, SynUser actor, ChangePlanView plan, ApproveChangePlanRequest? request = null) =>
        s.Fx.Service.ApproveChangePlanAsync(actor.Principal, s.Fx.Context, plan.Id, request ?? Approval(plan), _token);

    private static async Task<string> SnapshotAsync(Setup s, Guid id)
    {
        await using SqlConnection connection = s.Fx.Connection();
        string[] statements =
        [
            "SELECT * FROM svcacct.ChangePlans WHERE Id = @id ORDER BY Id FOR JSON PATH",
            "SELECT * FROM svcacct.ChangePlanAccounts WHERE PlanId = @id ORDER BY Id FOR JSON PATH",
            "SELECT * FROM svcacct.ChangePlanPreviews WHERE PlanId = @id ORDER BY Id FOR JSON PATH",
            "SELECT * FROM svcacct.ChangePlanItems WHERE PreviewId IN (SELECT Id FROM svcacct.ChangePlanPreviews WHERE PlanId = @id) ORDER BY Id FOR JSON PATH",
            "SELECT * FROM svcacct.ChangePlanApprovals WHERE PlanId = @id ORDER BY Id FOR JSON PATH",
            "SELECT * FROM svcacct.ChangeItemChecks WHERE ItemId IN (SELECT i.Id FROM svcacct.ChangePlanItems i JOIN svcacct.ChangePlanPreviews v ON v.Id = i.PreviewId WHERE v.PlanId = @id) ORDER BY Id FOR JSON PATH",
            "SELECT * FROM svcacct.ChangePlanEvents WHERE PlanId = @id ORDER BY Id FOR JSON PATH",
            "SELECT * FROM svcacct.History WHERE EntityType = 'ChangePlan' AND EntityId = @id ORDER BY Id FOR JSON PATH",
            "SELECT * FROM audit.AuditLog WHERE DetailsJson LIKE @pattern ORDER BY AuditLogId FOR JSON PATH"
        ];
        List<string> rows = [];
        foreach (string sql in statements)
        {
            rows.Add(string.Concat(await connection.QueryAsync<string>(sql, new { id, pattern = "%" + id.ToString("D") + "%" })));
        }

        return JsonSerializer.Serialize(rows);
    }

    private static void Hidden<T>(SaResult<T> result)
    {
        (result.ErrorCode, result.Field, result.Current).Should().Be((SaErrors.NotFound, (string?)null, (object?)null));
        result.Value.Should().BeNull();
    }

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }
}
