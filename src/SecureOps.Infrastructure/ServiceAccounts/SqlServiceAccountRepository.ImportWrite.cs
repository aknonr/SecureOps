using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.Import;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class SqlServiceAccountRepository
{
    /// <summary>Thrown inside a commit when a row version changed after planning; rolls the whole commit back.</summary>
    private sealed class StaleImportException() : Exception("Import target changed after planning.");

    private static async Task<ImportResultView> ExecuteWorkAsync(SqlConnection connection, SqlTransaction transaction, ImportBatchRecord batch,
        ImportWork work, SaActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        async Task Run(string sql, object parameters) =>
            await connection.ExecuteAsync(Cmd(sql, parameters, transaction, cancellationToken, _commitTimeoutSeconds));

        foreach (NewNamed org in work.Organizations)
        {
            await Run("""
                INSERT INTO svcacct.Organizations(Id, ParentId, Name, NormalizedName, Kind, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                VALUES(@Id, NULL, @Name, @Key, 'Other', @now, @UserId, @now, @UserId);
                """, new { org.Id, org.Name, org.Key, now, actor.UserId });
        }

        foreach (NewNamed team in work.Teams)
        {
            await Run("""
                INSERT INTO svcacct.Teams(Id, OrganizationId, Name, NormalizedName, Provisional, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                VALUES(@Id, NULL, @Name, @Key, 1, @now, @UserId, @now, @UserId);
                """, new { team.Id, team.Name, team.Key, now, actor.UserId });
        }

        foreach (NewNamed person in work.People)
        {
            await Run("""
                INSERT INTO svcacct.People(Id, DisplayName, NormalizedName, VerificationState, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                VALUES(@Id, @Name, @Key, 'Provisional', @now, @UserId, @now, @UserId);
                """, new { person.Id, person.Name, person.Key, now, actor.UserId });
        }

        foreach ((Guid personId, string alias, string key, string evidence) in work.PersonAliases)
        {
            await Run("""
                IF NOT EXISTS (SELECT 1 FROM svcacct.PersonAliases WHERE PersonId = @personId AND AliasNormalized = @key)
                    INSERT INTO svcacct.PersonAliases(Id, PersonId, Alias, AliasNormalized, Evidence, CreatedAt, CreatedBy)
                    VALUES(NEWID(), @personId, @alias, @key, @evidence, @now, @UserId);
                """, new { personId, alias, key, evidence, now, actor.UserId });
        }

        foreach (AccountCreate account in work.AccountCreates)
        {
            await Run("""
                INSERT INTO svcacct.Accounts(Id, AccountName, NormalizedName, Domain, NormalizedDomain, IdentityKey, IdentityState, ReportOrganizationId,
                    ConsumerTeamId, LifecycleState, Notes, LegacyReference, LegacyDisplayId, MigrationKey, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                VALUES(@Id, @Name, @NameKey, @Domain, @DomainKey, @IdentityKey, 'Provisional', @OrganizationId, @ConsumerTeamId, 'Active', @Notes,
                    @LegacyReference, @LegacyDisplayId, @MigrationKey, @now, @UserId, @now, @UserId);
                """, new
            {
                account.Id, account.Name, account.NameKey, account.Domain, account.DomainKey, account.IdentityKey, account.OrganizationId, account.ConsumerTeamId,
                account.Notes, account.LegacyReference, LegacyDisplayId = Truncate(account.LegacyDisplayId, 32), account.MigrationKey, now, actor.UserId
            });
        }

        foreach (AccountFill fill in work.AccountFills)
        {
            int changed = await connection.ExecuteAsync(Cmd("""
                UPDATE svcacct.Accounts SET Notes = COALESCE(Notes, @Notes), ConsumerTeamId = COALESCE(ConsumerTeamId, @ConsumerTeamId),
                    ReportOrganizationId = COALESCE(ReportOrganizationId, @OrganizationId), UpdatedAt = @now, UpdatedBy = @UserId
                WHERE Id = @AccountId AND RowVer = @RowVer;
                """, new { fill.Notes, fill.ConsumerTeamId, fill.OrganizationId, now, actor.UserId, fill.AccountId, RowVer = Version(fill.RowVersion) },
                transaction, cancellationToken));
            if (changed != 1)
            {
                throw new StaleImportException();
            }
        }

        foreach (AccountAliasAdd alias in work.AccountAliases)
        {
            await Run("""
                IF NOT EXISTS (SELECT 1 FROM svcacct.AccountAliases WHERE AccountId = @AccountId AND NormalizedAlias = @NormalizedAlias)
                    INSERT INTO svcacct.AccountAliases(Id, AccountId, Alias, NormalizedAlias, Domain, Source, ConfirmedBy, ConfirmedAt)
                    VALUES(NEWID(), @AccountId, @Alias, @NormalizedAlias, @Domain, @Source, @UserId, @now);
                """, new { alias.AccountId, alias.Alias, alias.NormalizedAlias, alias.Domain, Source = "Import " + batch.Id.ToString("D"), actor.UserId, now });
        }

        foreach (OwnershipAdd ownership in work.Ownerships)
        {
            bool confirm = ownership.State == OwnershipState.Confirmed;
            await Run("""
                INSERT INTO svcacct.OwnershipAssignments(Id, AccountId, TeamId, PersonId, State, Source, ProposedBy, ProposedAt, DecidedBy, DecidedAt,
                    DecisionReason, SourceKey)
                VALUES(@Id, @AccountId, @TeamId, @PersonId, @State, @Source, @UserId, @now, @DecidedBy, @DecidedAt, @Reason, @SourceKey);
                IF @confirm = 1
                    UPDATE svcacct.Accounts SET CurrentOwnerTeamId = @TeamId, CurrentOwnerPersonId = @PersonId, UpdatedAt = @now, UpdatedBy = @UserId
                    WHERE Id = @AccountId AND CurrentOwnerTeamId IS NULL AND CurrentOwnerPersonId IS NULL;
                """, new
            {
                ownership.Id, ownership.AccountId, ownership.TeamId, ownership.PersonId, State = ownership.State.ToString(), Source = Truncate(ownership.Source, 300),
                actor.UserId, now, DecidedBy = confirm ? actor.UserId : (Guid?)null, DecidedAt = confirm ? now : (DateTimeOffset?)null,
                Reason = confirm ? "İçe aktarma kararında yetkili onay" : null, ownership.SourceKey, confirm
            });
        }

        foreach (HandoverAdd handover in work.Handovers)
        {
            await Run("""
                INSERT INTO svcacct.Handovers(Id, AccountId, SourceTeamId, TargetTeamId, ConsumerTeamId, SourceBatchId, CohortLabel, ProposedOn, Status,
                    SourceNote, SourceKey, LegacyReference, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                VALUES(@Id, @AccountId, @SourceTeamId, @TargetTeamId, @ConsumerTeamId, @BatchId, @CohortLabel, @ProposedOn, 'Proposed',
                    @SourceNote, @SourceKey, @LegacyReference, @now, @UserId, @now, @UserId);
                IF @TrackGmsa = 1 AND NOT EXISTS (SELECT 1 FROM svcacct.IdentityTransitions WHERE AccountId = @AccountId AND Target = 'gMSA')
                    INSERT INTO svcacct.IdentityTransitions(Id, AccountId, HandoverId, Target, Suitability, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                    VALUES(NEWID(), @AccountId, @Id, 'gMSA', 'Unknown', @now, @UserId, @now, @UserId);
                """, new
            {
                handover.Id, handover.AccountId, handover.SourceTeamId, handover.TargetTeamId, handover.ConsumerTeamId, BatchId = batch.Id,
                CohortLabel = Truncate(handover.CohortLabel, 200), handover.ProposedOn, SourceNote = Truncate(handover.SourceNote, 1000), handover.SourceKey,
                handover.LegacyReference, handover.TrackGmsa, now, actor.UserId
            });
        }

        foreach (RequestAdd request in work.Requests)
        {
            bool closed = request.Status == ServiceAccountRequestStatus.Closed;
            await Run("""
                INSERT INTO svcacct.WorkRequests(Id, AccountId, ActionType, Status, CloseOutcome, TargetTeamId, FollowupPersonId, ContactPersonId, PlanStart,
                    PlanEnd, PlanAnnouncedOn, NextFollowupOn, FirstSentOn, LastReplyOn, Notes, SourceKey, LegacyReference, LegacyDisplayId, MigrationKey,
                    CloseReason, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                VALUES(@Id, @AccountId, @ActionType, @Status, @CloseOutcome, @TargetTeamId, @FollowupPersonId, @ContactPersonId, @PlanStart, @PlanEnd,
                    @PlanAnnouncedOn, @NextFollowupOn, @FirstSentOn, @LastReplyOn, @Notes, @SourceKey, @LegacyReference, @LegacyDisplayId, @MigrationKey,
                    @CloseReason, @now, @UserId, @now, @UserId);
                """, new
            {
                request.Id, request.AccountId, ActionType = request.ActionType.ToString(), Status = request.Status.ToString(),
                CloseOutcome = closed ? "Completed" : null, request.TargetTeamId, request.FollowupPersonId, request.ContactPersonId, request.PlanStart,
                request.PlanEnd, request.PlanAnnouncedOn, request.NextFollowupOn, request.FirstSentOn, request.LastReplyOn, Notes = Truncate(request.Notes, 4000),
                request.SourceKey, request.LegacyReference, LegacyDisplayId = Truncate(request.LegacyDisplayId, 32), request.MigrationKey,
                CloseReason = closed ? "Eski takip dosyasında kapalı; doğrulanmış kapanış kanıtı değildir." : null, now, actor.UserId
            });
        }

        foreach (ActionAdd action in work.Actions)
        {
            await Run("""
                INSERT INTO svcacct.ActionEvents(Id, AccountId, ActionType, Result, RecordKind, ActualOn, ActualPrecision, PerformerTeamId, PerformerPersonId,
                    EvidenceNote, VerifiedOn, VerifiedByPersonId, SourceNote, LegacyReference, LegacyDisplayId, MigrationKey, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                VALUES(@Id, @AccountId, @ActionType, @Result, @Kind, @ActualOn, @Precision, @PerformerTeamId, @PerformerPersonId, @EvidenceNote, @VerifiedOn,
                    @VerifiedByPersonId, @SourceNote, @LegacyReference, @LegacyDisplayId, @MigrationKey, @now, @UserId, @now, @UserId);
                """, new
            {
                action.Id, action.AccountId, ActionType = action.ActionType.ToString(), Result = action.Result.ToString(), Kind = action.Kind.ToString(),
                action.ActualOn, Precision = action.ActualOn is null ? "Unknown" : "DateOnly", action.PerformerTeamId, action.PerformerPersonId,
                EvidenceNote = Truncate(action.EvidenceNote, 2000), action.VerifiedOn, action.VerifiedByPersonId, SourceNote = Truncate(action.SourceNote, 2000),
                action.LegacyReference, LegacyDisplayId = Truncate(action.LegacyDisplayId, 32), action.MigrationKey, now, actor.UserId
            });
        }

        int links = 0;
        foreach (CommunicationAdd communication in work.Communications)
        {
            await Run("""
                INSERT INTO svcacct.Communications(Id, Direction, Kind, OccurredOn, Precision, ContactTeamId, Subject, Summary, Link, RecordScope,
                    MeaningfulReply, LegacyReference, MigrationKey, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                VALUES(@Id, @Direction, @Kind, @OccurredOn, @Precision, @ContactTeamId, @Subject, @Summary, @Link, @RecordScope, @MeaningfulReply,
                    @LegacyReference, @MigrationKey, @now, @UserId, @now, @UserId);
                """, new
            {
                communication.Id, Direction = communication.Direction.ToString(), Kind = communication.Kind.ToString(), communication.OccurredOn,
                Precision = communication.OccurredOn is null ? "Unknown" : "DateOnly", communication.ContactTeamId, Subject = Truncate(communication.Subject, 400),
                Summary = Truncate(communication.Summary, 4000), Link = Truncate(communication.Link, 400), communication.RecordScope, communication.MeaningfulReply,
                communication.LegacyReference, communication.MigrationKey, now, actor.UserId
            });
            links += await LinkAsync(connection, transaction, communication.Id, communication.Accounts, actor, now, cancellationToken);
        }

        foreach (CommunicationLinkAdd add in work.CommunicationLinks)
        {
            links += await LinkAsync(connection, transaction, add.CommunicationId, add.Accounts, actor, now, cancellationToken);
        }

        foreach (FindingAdd finding in work.Findings)
        {
            await Run("""
                INSERT INTO svcacct.Findings(Id, AccountId, Server, ComponentType, ComponentName, Environment, ScanOn, ScanResult, MatchResult, CoverageWindow,
                    EvidenceNote, OwningTeamId, Status, JobReference, Notes, LegacyReference, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                VALUES(@Id, @AccountId, @Server, @ComponentType, @ComponentName, @Environment, @ScanOn, @ScanResult, @MatchResult, @Coverage, @Evidence,
                    @OwningTeamId, @Status, @JobReference, @Notes, @LegacyReference, @now, @UserId, @now, @UserId);
                """, new
            {
                finding.Id, finding.AccountId, Server = Truncate(finding.Server, 256), ComponentType = Truncate(finding.ComponentType, 64),
                ComponentName = Truncate(finding.ComponentName, 256), Environment = Truncate(finding.Environment, 64), finding.ScanOn,
                ScanResult = finding.ScanResult.ToString(), MatchResult = finding.MatchResult.ToString(), Coverage = Truncate(finding.Coverage, 400),
                Evidence = Truncate(finding.Evidence, 2000), finding.OwningTeamId, Status = finding.Status.ToString(), JobReference = Truncate(finding.JobReference, 128),
                Notes = Truncate(finding.Notes, 2000), finding.LegacyReference, now, actor.UserId
            });
        }

        foreach (ReferenceAdd reference in work.References)
        {
            await Run("""
                DECLARE @recordId uniqueidentifier = (SELECT Id FROM svcacct.ExternalRecords WITH (UPDLOCK, HOLDLOCK)
                    WHERE RecordType = @Type AND NormalizedNumber = @Number);
                IF @recordId IS NULL
                BEGIN
                    SET @recordId = NEWID();
                    INSERT INTO svcacct.ExternalRecords(Id, RecordType, Number, NormalizedNumber, CreatedAt, CreatedBy)
                    VALUES(@recordId, @Type, @Number, @Number, @now, @UserId);
                END;
                IF NOT EXISTS (SELECT 1 FROM svcacct.ExternalRecordLinks WHERE ExternalRecordId = @recordId AND EntityType = @EntityType AND EntityId = @EntityId)
                    INSERT INTO svcacct.ExternalRecordLinks(ExternalRecordId, EntityType, EntityId, AccountId, LinkedAt, LinkedBy)
                    VALUES(@recordId, @EntityType, @EntityId, @AccountId, @now, @UserId);
                """, new { Type = reference.Type.ToString(), reference.Number, reference.EntityType, reference.EntityId, reference.AccountId, now, actor.UserId });
        }

        await ObservationsAsync(connection, transaction, batch, work.Observations, actor, now, cancellationToken);
        await ImportHistoryAsync(connection, transaction, batch, work, actor, now, cancellationToken);
        return new ImportResultView(work.AccountCreates.Count, work.Observations.Count(o => o.Presence == "Present"),
            work.Observations.Count(o => o.Presence == "NotPresent"), work.Ownerships.Count(o => o.State == OwnershipState.Proposed),
            work.Ownerships.Count(o => o.State == OwnershipState.Confirmed), work.Requests.Count, work.Actions.Count, work.Communications.Count, links,
            work.Findings.Count, work.Handovers.Count, work.Handovers.Count(h => h.TrackGmsa), work.Teams.Count, work.Organizations.Count, work.People.Count,
            work.Unchanged, work.Skipped, work.Invalid, now);
    }

    private static async Task<int> LinkAsync(SqlConnection connection, SqlTransaction transaction, Guid communicationId, IEnumerable<Guid> accounts,
        SaActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        int added = 0;
        foreach (Guid accountId in accounts.Distinct())
        {
            added += await connection.ExecuteAsync(Cmd("""
                IF NOT EXISTS (SELECT 1 FROM svcacct.CommunicationAccounts WHERE CommunicationId = @communicationId AND AccountId = @accountId)
                    INSERT INTO svcacct.CommunicationAccounts(CommunicationId, AccountId, LinkedAt, LinkedBy) VALUES(@communicationId, @accountId, @now, @UserId);
                """, new { communicationId, accountId, now, actor.UserId }, transaction, cancellationToken)) > 0 ? 1 : 0;
        }

        return added;
    }

    private static async Task ObservationsAsync(SqlConnection connection, SqlTransaction transaction, ImportBatchRecord batch,
        IReadOnlyList<ObservationAdd> observations, SaActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (observations.Count == 0)
        {
            return;
        }

        using DataTable table = new();
        foreach ((string name, Type type) in new[]
        {
            ("Id", typeof(Guid)), ("AccountId", typeof(Guid)), ("BatchId", typeof(Guid)), ("SourceProfile", typeof(string)), ("SourceReportDate", typeof(DateTime)),
            ("Presence", typeof(string)), ("PasswordLastSet", typeof(DateTime)), ("LastLogonAdOrLdap", typeof(DateTime)), ("LastLogonAd", typeof(DateTime)),
            ("Organization", typeof(string)), ("GroupDirectorate", typeof(string)), ("Comment", typeof(string)), ("SourceTeam", typeof(string)),
            ("ConsumerTeam", typeof(string)), ("HandoverFlag", typeof(string)), ("SourceRow", typeof(string)), ("RawRowJson", typeof(string)),
            ("RecordedAt", typeof(DateTimeOffset))
        })
        {
            table.Columns.Add(name, type);
        }

        object Db(object? value) => value ?? DBNull.Value;
        foreach (ObservationAdd o in observations)
        {
            table.Rows.Add(Guid.NewGuid(), o.AccountId, batch.Id, o.Profile, Db(batch.SourceReportDate?.ToDateTime(TimeOnly.MinValue)), o.Presence,
                Db(o.PasswordLastSet), Db(o.LastLogonAdOrLdap), Db(o.LastLogonAd), Db(Truncate(o.Organization, 200)), Db(Truncate(o.GroupDirectorate, 200)),
                Db(Truncate(o.Comment, 2000)), Db(Truncate(o.SourceTeam, 200)), Db(Truncate(o.ConsumerTeam, 200)), Db(Truncate(o.HandoverFlag, 32)),
                Db(Truncate(o.SourceRow, 120)), o.RawJson, now);
        }

        using SqlBulkCopy bulk = new(connection, SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.FireTriggers, transaction)
        {
            DestinationTableName = "svcacct.AccountObservations",
            BulkCopyTimeout = _commitTimeoutSeconds
        };
        foreach (DataColumn column in table.Columns)
        {
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulk.WriteToServerAsync(table, cancellationToken);
        if (batch.SourceReportDate is { } reportDate)
        {
            // An older period never moves the latest observation backwards; unknown dates never update it.
            await connection.ExecuteAsync(Cmd("""
                UPDATE a SET LastObservedOn = @reportDate, LastObservationPresence = o.Presence, UpdatedAt = @now, UpdatedBy = @UserId
                FROM svcacct.Accounts a JOIN svcacct.AccountObservations o ON o.AccountId = a.Id
                WHERE o.BatchId = @BatchId AND o.SourceProfile IN ('coordination-list','generic')
                  AND (a.LastObservedOn IS NULL OR a.LastObservedOn <= @reportDate);
                """, new { reportDate, now, actor.UserId, BatchId = batch.Id }, transaction, cancellationToken, _commitTimeoutSeconds));
        }
    }

    private static async Task ImportHistoryAsync(SqlConnection connection, SqlTransaction transaction, ImportBatchRecord batch, ImportWork work,
        SaActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        IEnumerable<(Guid AccountId, string Change)> changes = work.AccountCreates.Select(a => (a.Id, "Hesap oluşturuldu"))
            .Concat(work.Ownerships.Select(o => (o.AccountId, o.State == OwnershipState.Confirmed ? "Sahiplik onaylandı" : "Sahiplik önerildi")))
            .Concat(work.Requests.Select(r => (r.AccountId, "Talep eklendi")))
            .Concat(work.Actions.Select(a => (a.AccountId, "İşlem kaydı eklendi")))
            .Concat(work.Handovers.Select(h => (h.AccountId, "Devir önerisi eklendi")))
            .Concat(work.Findings.Select(f => (f.AccountId, "Bulgu eklendi")))
            .Concat(work.Observations.Select(o => (o.AccountId, o.Presence == "Present" ? "Kaynak gözlemi" : "Bu partide görülmedi (gözlem)")));
        using DataTable table = new();
        foreach ((string name, Type type) in new[]
        {
            ("EntityType", typeof(string)), ("EntityId", typeof(Guid)), ("AccountId", typeof(Guid)), ("Action", typeof(string)), ("ChangesJson", typeof(string)),
            ("ActorUserId", typeof(Guid)), ("CorrelationId", typeof(string)), ("OccurredAt", typeof(DateTimeOffset))
        })
        {
            table.Columns.Add(name, type);
        }

        foreach (IGrouping<Guid, (Guid AccountId, string Change)> account in changes.GroupBy(c => c.AccountId))
        {
            table.Rows.Add("Account", account.Key, account.Key, "Imported", JsonSerializer.Serialize(new
            {
                BatchId = batch.Id,
                batch.Profile,
                SourceReportDate = DateText(batch.SourceReportDate),
                Changes = account.GroupBy(c => c.Change).Select(g => g.Count() == 1 ? g.Key : $"{g.Key} ×{g.Count()}")
            }), actor.UserId, Truncate(actor.CorrelationId, 128), now);
        }

        using SqlBulkCopy bulk = new(connection, SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.FireTriggers, transaction)
        {
            DestinationTableName = "svcacct.History",
            BulkCopyTimeout = _commitTimeoutSeconds
        };
        foreach (DataColumn column in table.Columns)
        {
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulk.WriteToServerAsync(table, cancellationToken);
    }
}
