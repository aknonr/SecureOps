using System.Text.Json;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.Import;

public sealed partial class ImportPlanner
{
    private PlannedRow PlanOwnership(int key, StagedRow row)
    {
        AccountTarget account = Find(row[StagedFields.Account]);
        if (row.Errors.Count > 0 || account.Id is not { } accountId)
        {
            return Invalid(key, row, account.Label, account.Error ?? "Invalid");
        }

        (Guid Id, bool New)? team = Team(row[StagedFields.OwnerTeam]);
        (Guid Id, bool New)? person = ResolvePerson(row[StagedFields.OwnerPerson], out IReadOnlyList<Guid> ambiguous);
        List<ImportCandidate> candidates = [.. ambiguous.Select(id => new ImportCandidate(id, row[StagedFields.OwnerPerson] ?? "?", "Aynı ad birden fazla kişide; kimlik ile seçin"))];
        if (ambiguous.Count > 0 && DecisionTarget(key) is { } chosen && ambiguous.Contains(chosen))
        {
            person = (chosen, false);
        }

        if (team is null && person is null && ambiguous.Count == 0)
        {
            return Row(key, row, ServiceAccountImportClasses.Same, account, [], [], _applyOrSkip, ImportDecisions.Apply, false, "no-owner");
        }

        List<ImportFieldDiff> diff =
        [
            new("Sahip ekip", account.Existing?.OwnerTeamId is null ? null : "mevcut onaylı", team is null ? null : row[StagedFields.OwnerTeam], "Öneri"),
            new("Sorumlu kişi", account.Existing?.OwnerPersonId is null ? null : "mevcut onaylı", person is null ? null : row[StagedFields.OwnerPerson], "Öneri (kimlik doğrulanmadı)")
        ];
        string sourceKey = "own:" + accountId.ToString("N") + ":" + (team?.Id.ToString("N") ?? "-") + ":" + (person?.Id.ToString("N") ?? "-");
        object fingerprint = new { team = Fp(team, row[StagedFields.OwnerTeam]), person = Fp(person, row[StagedFields.OwnerPerson]) };
        ContextAccount? existing = account.Existing;
        bool matchesConfirmed = existing is not null && (existing.OwnerTeamId is not null || existing.OwnerPersonId is not null)
            && (team is null || team.Value.Id == existing.OwnerTeamId) && (person is null || person.Value.Id == existing.OwnerPersonId);
        if (matchesConfirmed || _context.SourceKeys.Contains(sourceKey) || !_plannedKeys.Add(sourceKey))
        {
            return Row(key, row, ServiceAccountImportClasses.Same, account, diff, [], _applyOrSkip, ImportDecisions.Apply, false, fingerprint);
        }

        bool conflict = existing is not null && (existing.OwnerTeamId is not null || existing.OwnerPersonId is not null);
        IReadOnlyList<string> allowed = conflict ? [ImportDecisions.Keep, ImportDecisions.Propose] : [ImportDecisions.Propose, ImportDecisions.Confirm, ImportDecisions.Skip];
        string decision = DecisionFor(key, allowed[0], allowed);
        if (person is null && ambiguous.Count > 0 && team is null)
        {
            decision = ImportDecisions.Skip;
        }

        if (decision is ImportDecisions.Propose or ImportDecisions.Confirm)
        {
            _work.Ownerships.Add(new OwnershipAdd(Guid.NewGuid(), accountId, team?.Id, person?.Id,
                decision == ImportDecisions.Confirm ? OwnershipState.Confirmed : OwnershipState.Proposed,
                "Kaynak beyanı: " + _input.SourceLabel + " / " + row.Sheet + " / satır " + row.RowNumber, sourceKey, key));
        }

        return Row(key, row, conflict ? ServiceAccountImportClasses.Conflict : ServiceAccountImportClasses.New, account, diff,
            ambiguous.Count > 0 ? ["AmbiguousPerson"] : [], allowed, allowed[0], conflict || ambiguous.Count > 0, fingerprint, candidates);
    }

    private PlannedRow PlanRequest(int key, StagedRow row)
    {
        AccountTarget account = Find(row[StagedFields.Account]);
        List<string> warnings = [];
        if (row.Errors.Count > 0 || account.Id is not { } accountId)
        {
            return Invalid(key, row, account.Label, account.Error ?? "Invalid");
        }

        if (Replayed(StagedKinds.Request, row))
        {
            return Row(key, row, ServiceAccountImportClasses.Same, account, [], warnings, _createOrSkip, ImportDecisions.Create, false, "same");
        }

        ServiceAccountActionType? type = ServiceAccountLabels.ParseAction(row[StagedFields.ActionType]);
        ServiceAccountRequestStatus? status = row[StagedFields.Status] is null ? ServiceAccountRequestStatus.Open : ServiceAccountLabels.ParseRequestStatus(row[StagedFields.Status]);
        DateOnly? start = Date(row, StagedFields.PlanStart), end = Date(row, StagedFields.PlanEnd);
        if (type is null || status is null || ServiceAccountRules.ValidatePlan(start, end) is not null)
        {
            return Invalid(key, row, account.Label, type is null ? "UnknownActionType" : status is null ? "UnknownStatus" : "PlanEndBeforeStart");
        }

        (Guid Id, bool New)? target = Team(row[StagedFields.TargetTeam]);
        string? sourceKey = type == ServiceAccountActionType.GmsaHandover && target is not null
            ? "request:" + accountId.ToString("N") + ":GmsaHandover:" + target.Value.Id.ToString("N") : null;
        if (sourceKey is not null && (_context.SourceKeys.Contains(sourceKey) || !_plannedKeys.Add(sourceKey)))
        {
            sourceKey = null;
            warnings.Add("SimilarGmsaRequestExists");
        }

        List<ImportFieldDiff> diff =
        [
            new("Beklenen aksiyon", null, ServiceAccountLabels.Action(type.Value), "Yeni talep"),
            new("Plan", null, start is null && end is null ? "Tarih bekleniyor" : $"{Text(start)} – {Text(end)}", "Yeni talep"),
            new("Takip sorumlusu (sahip değil)", null, row[StagedFields.FollowupPerson], "Yeni talep")
        ];
        if (DecisionFor(key, ImportDecisions.Create, _createOrSkip) == ImportDecisions.Create)
        {
            var id = Guid.NewGuid();
            _work.Requests.Add(new RequestAdd(id, accountId, type.Value, status.Value, target?.Id,
                PersonOrWarn(row[StagedFields.FollowupPerson], "FollowupPerson", warnings), PersonOrWarn(row[StagedFields.ContactPerson], "ContactPerson", warnings),
                start, end, Date(row, StagedFields.PlanAnnounced), Date(row, StagedFields.NextFollowup), Date(row, StagedFields.FirstSent), Date(row, StagedFields.LastReply),
                ServiceAccountText.Clean(row[StagedFields.Notes]), sourceKey, row.LegacyReference, row[StagedFields.LegacyId], row.MigrationKey));
            AddReference(ExternalRecordType.OR, row[StagedFields.Or], "Request", id, accountId);
            AddReference(OtherType(row[StagedFields.OtherRecord]), row[StagedFields.OtherRecord], "Request", id, accountId);
        }

        return Row(key, row, ServiceAccountImportClasses.New, account, diff, warnings, _createOrSkip, ImportDecisions.Create, false,
            new { target = Fp(target, row[StagedFields.TargetTeam]), gmsaKey = sourceKey is not null });
    }

    private PlannedRow PlanAction(int key, StagedRow row)
    {
        AccountTarget account = Find(row[StagedFields.Account]);
        List<string> warnings = [];
        if (row.Errors.Count > 0 || account.Id is not { } accountId)
        {
            return Invalid(key, row, account.Label, account.Error ?? "Invalid");
        }

        if (Replayed(StagedKinds.Action, row))
        {
            return Row(key, row, ServiceAccountImportClasses.Same, account, [], warnings, _createOrSkip, ImportDecisions.Create, false, "same");
        }

        ServiceAccountActionType? type = ServiceAccountLabels.ParseAction(row[StagedFields.ActionType]);
        ServiceAccountActionResult? result = ServiceAccountLabels.ParseResult(row[StagedFields.Result]);
        ServiceAccountRecordKind kind = ServiceAccountLabels.ParseKind(row[StagedFields.RecordKind]) ?? ServiceAccountRecordKind.Intermediate;
        if (type is null || result is null)
        {
            return Invalid(key, row, account.Label, type is null ? "UnknownActionType" : "UnknownResult");
        }

        DateOnly? actual = Date(row, StagedFields.ActualDate), verified = Date(row, StagedFields.VerifiedDate);
        string? sourceNote = ServiceAccountText.Clean(row[StagedFields.SourceNote]);
        if (verified is { } v && actual is { } a && v < a)
        {
            warnings.Add("VerificationBeforeActionKeptAsNote");
            sourceNote = string.Join(" | ", new[] { sourceNote, "Kaynak doğrulama tarihi işlemden önce: " + Text(v) }.OfType<string>());
            verified = null;
        }

        if (actual is null && result != ServiceAccountActionResult.Planned)
        {
            warnings.Add("UndatedActionCountedSeparately");
        }

        List<ImportFieldDiff> diff =
        [
            new("İşlem", null, ServiceAccountLabels.Action(type.Value) + " / " + ServiceAccountLabels.Result(result.Value), "Yeni işlem"),
            new("İşlem tarihi", null, actual is null ? "Bilinmiyor" : Text(actual), "Yeni işlem"),
            new("Kayıt türü", null, ServiceAccountLabels.Kind(kind), "Yeni işlem")
        ];
        if (DecisionFor(key, ImportDecisions.Create, _createOrSkip) == ImportDecisions.Create)
        {
            var id = Guid.NewGuid();
            _work.Actions.Add(new ActionAdd(id, accountId, type.Value, result.Value, kind, actual, Team(row[StagedFields.PerformerTeam])?.Id,
                PersonOrWarn(row[StagedFields.PerformerPerson], "PerformerPerson", warnings), ServiceAccountText.Clean(row[StagedFields.Evidence]), verified,
                verified is null ? null : PersonOrWarn(row[StagedFields.Verifier], "Verifier", warnings), sourceNote, row.LegacyReference, row[StagedFields.LegacyId], row.MigrationKey));
            AddReference(ExternalRecordType.OR, row[StagedFields.Or], "Action", id, accountId);
            AddReference(ExternalRecordType.OCO, row[StagedFields.Oco], "Action", id, accountId);
        }

        return Row(key, row, ServiceAccountImportClasses.New, account, diff, warnings, _createOrSkip, ImportDecisions.Create, false, "action");
    }

    private PlannedRow PlanCommunication(int key, StagedRow row)
    {
        List<string> warnings = [];
        List<Guid> accounts = [];
        List<string> names = [.. new[] { row[StagedFields.Account] }.OfType<string>()];
        if (row[StagedFields.LinkedAccounts] is { } json)
        {
            names.AddRange(JsonSerializer.Deserialize<string[]>(json) ?? []);
        }

        foreach (string name in names.Distinct(StringComparer.Ordinal))
        {
            AccountTarget target = Find(name);
            if (target.Id is { } id)
            {
                accounts.Add(id);
            }
            else
            {
                warnings.Add("LinkedAccountNotLinked:" + target.Error);
            }
        }

        accounts = [.. accounts.Distinct()];
        string[] stable = [.. accounts.Select(Stable).Order(StringComparer.Ordinal)];
        AccountTarget first = new(accounts.Count == 1 ? accounts[0] : null, names.Count == 1 ? names[0] : names.Count + " hesap", null, null,
            string.Join(",", stable), null, [], false);
        CommunicationDirection? direction = ServiceAccountLabels.ParseDirection(row[StagedFields.Direction]);
        CommunicationKind? kind = ServiceAccountLabels.ParseCommunicationKind(row[StagedFields.CommunicationKind]);
        if (row.Errors.Count > 0 || direction is null || kind is null)
        {
            return Invalid(key, row, first.Label, direction is null ? "UnknownDirection" : "UnknownCommunicationKind");
        }

        if (row.LegacyReference is { } legacy && _context.CommunicationsByLegacy.TryGetValue(legacy, out ContextCommunication? existing))
        {
            Guid[] missing = [.. accounts.Except(existing.Accounts)];
            if (missing.Length == 0)
            {
                return Row(key, row, ServiceAccountImportClasses.Same, first, [], warnings, _applyOrSkip, ImportDecisions.Apply, false, "same");
            }

            if (DecisionFor(key, ImportDecisions.Apply, _applyOrSkip) == ImportDecisions.Apply)
            {
                _work.CommunicationLinks.Add(new CommunicationLinkAdd(existing.Id, missing));
            }

            return Row(key, row, ServiceAccountImportClasses.Update, first, [new("Hesap bağlantısı", existing.Accounts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                (existing.Accounts.Count + missing.Length).ToString(System.Globalization.CultureInfo.InvariantCulture), "Bağlantı eklenecek (mail sayısı değişmez)")],
                warnings, _applyOrSkip, ImportDecisions.Apply, false, missing.Select(Stable).Order(StringComparer.Ordinal));
        }

        if (row.LegacyReference is { } planned && !_plannedKeys.Add("Communication|" + planned))
        {
            return Row(key, row, ServiceAccountImportClasses.Same, first, [], warnings, _createOrSkip, ImportDecisions.Create, false, "same");
        }

        DateOnly? occurred = Date(row, StagedFields.OccurredOn);
        List<ImportFieldDiff> diff =
        [
            new("Yazışma", null, ServiceAccountLabels.Direction(direction.Value) + " / " + ServiceAccountLabels.CommunicationKindLabel(kind.Value), "Yeni yazışma (1 mail)"),
            new("Tarih", null, occurred is null ? "Tarihsiz (haftalık sayıma girmez)" : Text(occurred), "Yeni yazışma"),
            new("Bağlı hesap", null, accounts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), "Bağlantı")
        ];
        if (DecisionFor(key, ImportDecisions.Create, _createOrSkip) == ImportDecisions.Create)
        {
            var id = Guid.NewGuid();
            bool team = ServiceAccountText.Same(row[StagedFields.RecordScope], "Ekip") || accounts.Count == 0;
            bool? meaningful = ServiceAccountText.Same(row[StagedFields.MeaningfulReply], "Evet") ? true
                : ServiceAccountText.Same(row[StagedFields.MeaningfulReply], "Hayır") ? false : null;
            string? summary = ServiceAccountText.Clean(row[StagedFields.Summary]);
            if (ServiceAccountText.Clean(row[StagedFields.EnteredBy]) is { } enteredBy)
            {
                summary = string.Join(" | ", new[] { summary, "Kaydı giren (kaynak): " + enteredBy }.OfType<string>());
            }

            _work.Communications.Add(new CommunicationAdd(id, direction.Value, kind.Value, occurred, Team(row[StagedFields.ContactTeam])?.Id,
                ServiceAccountText.Clean(row[StagedFields.Subject]), summary, ServiceAccountText.Clean(row[StagedFields.Link]), team ? "Team" : "Account",
                meaningful, accounts, row.LegacyReference, row.MigrationKey));
            AddReference(ExternalRecordType.OR, row[StagedFields.Or], "Communication", id, accounts.Count == 1 ? accounts[0] : null);
        }

        return Row(key, row, ServiceAccountImportClasses.New, first, diff, warnings, _createOrSkip, ImportDecisions.Create, false, stable);
    }

    private PlannedRow PlanFinding(int key, StagedRow row)
    {
        AccountTarget account = Find(row[StagedFields.Account]);
        if (row.Errors.Count > 0 || account.Id is not { } accountId)
        {
            return Invalid(key, row, account.Label, account.Error ?? "Invalid");
        }

        if (Replayed(StagedKinds.Finding, row))
        {
            return Row(key, row, ServiceAccountImportClasses.Same, account, [], [], _createOrSkip, ImportDecisions.Create, false, "same");
        }

        FindingScanResult scan = Label(row[StagedFields.ScanResult], FindingScanResult.Unknown,
            ("Başarılı", FindingScanResult.Success), ("Erişilemedi", FindingScanResult.Unreachable), ("Başarısız", FindingScanResult.Failed), ("Kısmi", FindingScanResult.Partial));
        FindingMatchResult match = Label(row[StagedFields.MatchResult], FindingMatchResult.Uncertain,
            ("Var", FindingMatchResult.Match), ("Yok", FindingMatchResult.NoMatch));
        FindingStatus status = Label(row[StagedFields.FindingStatus], FindingStatus.Open,
            ("İnceleniyor", FindingStatus.InReview), ("Kapalı", FindingStatus.Closed));
        if (DecisionFor(key, ImportDecisions.Create, _createOrSkip) == ImportDecisions.Create)
        {
            _work.Findings.Add(new FindingAdd(Guid.NewGuid(), accountId, row[StagedFields.Server], row[StagedFields.ComponentType], row[StagedFields.ComponentName],
                row[StagedFields.Environment], Stamp(row, StagedFields.ScanAt) is { } at ? DateOnly.FromDateTime(at) : null, scan, match, row[StagedFields.Coverage],
                row[StagedFields.Evidence], Team(row[StagedFields.OwningTeam])?.Id, status, row[StagedFields.JobReference], row[StagedFields.Notes], row.LegacyReference));
        }

        return Row(key, row, ServiceAccountImportClasses.New, account, [new("Bulgu", null, row[StagedFields.Server], "Yeni bulgu (işlem değildir)")], [],
            _createOrSkip, ImportDecisions.Create, false, "finding");
    }

    private PlannedRow PlanHandover(int key, StagedRow row)
    {
        AccountTarget account = Find(row[StagedFields.Account]);
        List<string> warnings = [];
        if (row.Errors.Count > 0 || account.Id is not { } accountId)
        {
            return Invalid(key, row, account.Label, account.Error ?? "Invalid");
        }

        if (Team(row[StagedFields.TargetTeam]) is not { } target)
        {
            return Invalid(key, row, account.Label, "TargetTeamMissing");
        }

        string sourceKey = "handover:" + accountId.ToString("N") + ":" + target.Id.ToString("N");
        if (Replayed(StagedKinds.Handover, row) || _context.SourceKeys.Contains(sourceKey) || !_plannedKeys.Add(sourceKey))
        {
            return Row(key, row, ServiceAccountImportClasses.Same, account, [], warnings, _createOrSkip, ImportDecisions.Create, false, "same");
        }

        string? note = ServiceAccountText.Clean(row[StagedFields.SourceNote]);
        if (Date(row, StagedFields.AcceptedOn) is { } accepted)
        {
            warnings.Add("SourceAcceptanceDateIsNotAcceptanceEvidence");
            note = string.Join(" | ", new[] { note, "Kaynakta kabul tarihi: " + Text(accepted) + " (yetkili kabul kaydı bekleniyor)" }.OfType<string>());
        }

        if (ServiceAccountText.Clean(row[StagedFields.SuitabilityNote]) is { } suitability)
        {
            note = string.Join(" | ", new[] { note, suitability }.OfType<string>());
        }

        bool gmsa = ServiceAccountLabels.ParseAction(row[StagedFields.TargetAction]) == ServiceAccountActionType.GmsaHandover
            && !_context.AccountsWithGmsaTransition.Contains(accountId) && _gmsaPlanned.Add(accountId);
        if (DecisionFor(key, ImportDecisions.Create, _createOrSkip) == ImportDecisions.Create)
        {
            _work.Handovers.Add(new HandoverAdd(Guid.NewGuid(), accountId, Team(row[StagedFields.SourceTeam])?.Id, target.Id, Team(row[StagedFields.ConsumerTeam])?.Id,
                ServiceAccountText.Clean(row[StagedFields.HandoverStatus]) is null ? null : _input.SourceLabel, Date(row, StagedFields.ProposedOn), note, sourceKey,
                row.LegacyReference, gmsa));
        }

        return Row(key, row, ServiceAccountImportClasses.New, account,
            [new("Devir", null, row[StagedFields.TargetTeam], "Devir kapsamına bildirildi (kabul değil)"),
             new("gMSA takibi", null, gmsa ? "Uygunluk bilinmiyor" : "—", "Takip (uygunluk/geçiş değil)")],
            warnings, _createOrSkip, ImportDecisions.Create, false, new { target = Fp(target, row[StagedFields.TargetTeam]), gmsa });
    }

    private static T Label<T>(string? label, T fallback, params (string Label, T Value)[] options) =>
        options.Where(o => ServiceAccountText.Same(label, o.Label)).Select(o => o.Value).DefaultIfEmpty(fallback).First();

    private ImportSummary Summary(List<PlannedRow> rows)
    {
        int Count(string c) => rows.Count(r => r.Classification == c);
        var usedTeams = ((Guid?[])[.. _work.AccountCreates.Select(a => a.ConsumerTeamId), .. _work.AccountFills.Select(a => a.ConsumerTeamId),
            .. _work.Ownerships.Select(o => o.TeamId), .. _work.Requests.Select(r => r.TargetTeamId), .. _work.Actions.Select(a => a.PerformerTeamId),
            .. _work.Communications.Select(c => c.ContactTeamId), .. _work.Findings.Select(f => f.OwningTeamId),
            .. _work.Handovers.SelectMany(h => new[] { h.SourceTeamId, h.TargetTeamId, h.ConsumerTeamId })])
            .OfType<Guid>().ToHashSet();
        _work.Teams.RemoveAll(t => !usedTeams.Contains(t.Id));
        HashSet<Guid> usedOrgs = [.. _work.AccountCreates.Select(a => a.OrganizationId).Concat(_work.AccountFills.Select(a => a.OrganizationId)).OfType<Guid>()];
        _work.Organizations.RemoveAll(o => !usedOrgs.Contains(o.Id));
        var usedPeople = ((Guid?[])[.. _work.Ownerships.Select(o => o.PersonId), .. _work.Requests.SelectMany(r => new[] { r.FollowupPersonId, r.ContactPersonId }),
            .. _work.Actions.SelectMany(a => new[] { a.PerformerPersonId, a.VerifiedByPersonId }), .. _work.PersonAliases.Select(a => (Guid?)a.PersonId)])
            .OfType<Guid>().ToHashSet();
        _work.People.RemoveAll(p => !usedPeople.Contains(p.Id));
        return new ImportSummary(rows.Count, Count(ServiceAccountImportClasses.New), Count(ServiceAccountImportClasses.ObservationUpdate),
            Count(ServiceAccountImportClasses.Update), Count(ServiceAccountImportClasses.Same), Count(ServiceAccountImportClasses.Conflict),
            Count(ServiceAccountImportClasses.Invalid), Count(ServiceAccountImportClasses.NotSeen), Count(ServiceAccountImportClasses.OutOfScope),
            rows.Count(r => r.RequiresDecision), rows.Count(r => r.RequiresDecision && _decisions.ContainsKey(r.RowKey)),
            rows.GroupBy(r => r.Row.Kind).ToDictionary(g => g.Key, g => g.Count()),
            [.. _work.Teams.Select(t => t.Name).Order(StringComparer.Ordinal)], [.. _work.Organizations.Select(o => o.Name).Order(StringComparer.Ordinal)],
            _file.IgnoredHelperColumns, _file.FormulaCells,
            // The cohort is accounts, not rows: the flagged source row and the handover row of one account count once.
            rows.Where(r => r.Row.Kind == StagedKinds.Handover || ServiceAccountText.Same(r.Row[StagedFields.HandoverFlag], "OK"))
                .Select(r => ServiceAccountText.AccountKey(r.Row[StagedFields.Account])).OfType<string>().Distinct(StringComparer.Ordinal).Count());
    }
}
