using System.Text.Json;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.Import;

public sealed partial class ImportPlanner
{
    private static readonly string[] _createOrSkip = [ImportDecisions.Create, ImportDecisions.Skip];
    private static readonly string[] _applyOrSkip = [ImportDecisions.Apply, ImportDecisions.Skip];

    private bool Replayed(string kind, StagedRow row) =>
        row.LegacyReference is { } legacy && (_context.LegacyReferences.Contains(kind + "|" + legacy) || !_plannedKeys.Add(kind + "|" + legacy));

    private PlannedRow Row(int key, StagedRow row, string classification, AccountTarget account, List<ImportFieldDiff> diff, List<string> warnings,
        IReadOnlyList<string> allowed, string defaultDecision, bool requiresDecision, object extra, IReadOnlyList<ImportCandidate>? candidates = null) =>
        new(key, row, classification, account.Id, account.Label, diff, row.Errors, [.. row.Warnings.Concat(warnings).Distinct()],
            candidates ?? account.Candidates, requiresDecision, defaultDecision, allowed,
            Hash(new { classification, account.Fingerprint, diff, allowed, requiresDecision, cand = (candidates ?? account.Candidates).Select(c => c.Id), extra }));

    private PlannedRow PlanAccount(int key, StagedRow row)
    {
        AccountTarget account = IdentityFor(key, row);
        if (Blocked(row, account, duplicateIsError: true) is { } blocked)
        {
            return Invalid(key, row, account.Label, blocked);
        }

        List<string> warnings = [];
        (Guid Id, bool New)? org = Organization(row[StagedFields.Organization], out string? orgError);
        (Guid Id, bool New)? consumer = Team(row[StagedFields.ConsumerTeam]);
        if (account.Existing is { } existing)
        {
            if (Replayed(StagedKinds.Account, row))
            {
                return Row(key, row, ServiceAccountImportClasses.Same, account, [], warnings, _applyOrSkip, ImportDecisions.Apply, false, "same");
            }

            List<ImportFieldDiff> diff = [];
            string? notes = Fill("Açıklama", existing.Notes, ServiceAccountText.Clean(row[StagedFields.Notes]), diff);
            Guid? fillConsumer = existing.ConsumerTeamId is null && consumer is not null ? consumer.Value.Id : null;
            Fill("Kullanan ekip", existing.ConsumerTeamId is null ? null : "mevcut", consumer is null ? null : row[StagedFields.ConsumerTeam], diff);
            Guid? fillOrg = existing.OrganizationId is null && org is not null ? org.Value.Id : null;
            Fill("Rapor organizasyonu", existing.OrganizationId is null ? null : "mevcut", org is null ? null : row[StagedFields.Organization], diff);
            bool changes = diff.Any(d => d.Effect == "Eklenecek") || row[StagedFields.Or] is not null || row[StagedFields.Oco] is not null;
            string decision = DecisionFor(key, ImportDecisions.Apply, _applyOrSkip);
            if (changes && decision == ImportDecisions.Apply)
            {
                _work.AccountFills.Add(new AccountFill(existing.Id, existing.RowVersion, notes, fillConsumer, fillOrg, key));
                AddReference(ExternalRecordType.OR, row[StagedFields.Or], "Account", existing.Id, existing.Id);
                AddReference(ExternalRecordType.OCO, row[StagedFields.Oco], "Account", existing.Id, existing.Id);
            }

            return Row(key, row, changes ? ServiceAccountImportClasses.Update : ServiceAccountImportClasses.Same, account, diff, warnings,
                _applyOrSkip, ImportDecisions.Apply, false, new { fillOrg, fillConsumer });
        }

        NewAccount stub = account.Stub!;
        if (orgError is not null)
        {
            stub.Created = false;
            return Invalid(key, row, account.Label, orgError);
        }

        if (!_context.Scope.All && (org is null || !_context.Scope.Organizations.Contains(org.Value.Id)))
        {
            stub.Created = false;
            return Invalid(key, row, account.Label, "OutOfScope");
        }

        List<ImportFieldDiff> created =
        [
            new("Hesap", null, stub.Name, "Yeni hesap"),
            new("Rapor organizasyonu", null, org is null ? null : row[StagedFields.Organization], org?.New == true ? "Yeni organizasyon" : "Eklenecek"),
            new("Kullanan ekip", null, consumer is null ? null : row[StagedFields.ConsumerTeam], "Eklenecek")
        ];
        if (stub.Created)
        {
            _work.AccountCreates.Add(new AccountCreate(stub.Id, stub.Name, stub.NameKey, stub.Domain, stub.DomainKey, stub.IdentityKey, org?.Id, consumer?.Id,
                ServiceAccountText.Clean(row[StagedFields.Notes]), row.LegacyReference, row[StagedFields.LegacyId], row.MigrationKey, key));
            AddReference(ExternalRecordType.OR, row[StagedFields.Or], "Account", stub.Id, stub.Id);
            AddReference(ExternalRecordType.OCO, row[StagedFields.Oco], "Account", stub.Id, stub.Id);
        }

        IReadOnlyList<string> allowed = account.Candidates.Count > 0 ? [ImportDecisions.Create, ImportDecisions.Link, ImportDecisions.Skip] : _createOrSkip;
        if (account.BatchVariant is { } variant)
        {
            warnings.Add("BatchSpellingVariant:" + variant);
        }

        return Row(key, row, account.NeedsDecision ? ServiceAccountImportClasses.Conflict : ServiceAccountImportClasses.New, account, created, warnings,
            allowed, ImportDecisions.Create, account.NeedsDecision, new { org = Fp(org, row[StagedFields.Organization]), consumer = Fp(consumer, row[StagedFields.ConsumerTeam]) });
    }

    /// <summary>Returns a blocking error, or null. A skipped owning row is a decision, not an error.</summary>
    private static string? Blocked(StagedRow row, AccountTarget account, bool duplicateIsError)
    {
        if (row.Errors.Count > 0)
        {
            if (account.OwnsCreation && account.Stub is { } stub)
            {
                stub.Created = false;
            }

            return row.Errors[0];
        }

        if (account.Error is "OutOfScope" or "AccountNameMissing")
        {
            return account.Error;
        }

        if (!account.OwnsCreation && account.Stub is { } planned)
        {
            return duplicateIsError ? "DuplicateInFile" : planned.Created ? null : "AccountSkippedInRow:" + planned.RowKey;
        }

        return null;
    }

    private static string? Fill(string field, string? current, string? proposed, List<ImportFieldDiff> diff)
    {
        if (proposed is null)
        {
            return null;
        }

        if (current is null)
        {
            diff.Add(new ImportFieldDiff(field, null, proposed, "Eklenecek"));
            return proposed;
        }

        diff.Add(new ImportFieldDiff(field, current, proposed, string.Equals(current, proposed, StringComparison.Ordinal) ? "Aynı" : "Korunur (mevcut değer)"));
        return null;
    }

    private PlannedRow PlanObservation(int key, StagedRow row)
    {
        AccountTarget account = IdentityFor(key, row);
        if (Blocked(row, account, duplicateIsError: false) is { } blocked)
        {
            return Invalid(key, row, account.Label, blocked);
        }

        string profile = row[StagedFields.ObservationProfile] ?? _input.Profile;
        List<string> warnings = [];
        if (account.Stub is { } stub && account.OwnsCreation)
        {
            (Guid Id, bool New)? org = Organization(row[StagedFields.Organization], out string? orgError);
            if (orgError is not null || !_context.Scope.All && (org is null || !_context.Scope.Organizations.Contains(org.Value.Id)))
            {
                stub.Created = false;
                return Invalid(key, row, account.Label, orgError ?? "OutOfScope");
            }

            if (stub.Created)
            {
                _work.AccountCreates.Add(new AccountCreate(stub.Id, stub.Name, stub.NameKey, stub.Domain, stub.DomainKey, stub.IdentityKey, org?.Id,
                    null, null, null, null, null, key));
            }
        }

        ContextObservation? latest = account.Existing is { } existing
            ? _context.LatestObservations.FirstOrDefault(o => o.AccountId == existing.Id && o.Profile == profile) : null;
        if (latest?.SourceReportDate is { } previous && _input.SourceReportDate is { } current && current < previous)
        {
            warnings.Add("OlderThanLatestObservation");
        }

        List<ImportFieldDiff> diff = [.. _observationFields.Select(f => new ImportFieldDiff(f.Label,
            latest?.Values.GetValueOrDefault(f.Field), row[f.Field] ?? row[f.Field + "Text"], "Gözlem"))
            .Where(d => d.Proposed is not null || d.Current is not null)];
        string decision = DecisionFor(key, account.OwnsCreation ? ImportDecisions.Create : ImportDecisions.Apply,
            account.OwnsCreation ? (account.Candidates.Count > 0 ? [ImportDecisions.Create, ImportDecisions.Link, ImportDecisions.Skip] : _createOrSkip) : _applyOrSkip);
        if (account.Id is { } accountId && decision != ImportDecisions.Skip)
        {
            _observed.Add((accountId, profile));
            _work.Observations.Add(Observation(accountId, profile, "Present", row));
            if (_input.Profile == ServiceAccountImportProfiles.DbaHandover && ServiceAccountText.Same(row[StagedFields.HandoverFlag], "OK"))
            {
                PlanDbaHandover(accountId, row, warnings);
            }
        }

        if (account.BatchVariant is { } variant)
        {
            warnings.Add("BatchSpellingVariant:" + variant);
        }

        string classification = account.OwnsCreation
            ? account.NeedsDecision ? ServiceAccountImportClasses.Conflict : ServiceAccountImportClasses.New
            : account.Stub is not null ? ServiceAccountImportClasses.New : ServiceAccountImportClasses.ObservationUpdate;
        IReadOnlyList<string> allowed = account.OwnsCreation
            ? account.Candidates.Count > 0 ? [ImportDecisions.Create, ImportDecisions.Link, ImportDecisions.Skip] : _createOrSkip
            : _applyOrSkip;
        return Row(key, row, classification, account, diff, warnings, allowed, allowed[0], account.NeedsDecision,
            new { profile, latest = latest?.SourceReportDate });
    }

    private void PlanDbaHandover(Guid accountId, StagedRow row, List<string> warnings)
    {
        if (Team(_input.TargetTeam) is not { } target)
        {
            warnings.Add("HandoverTargetTeamNotDeclared");
            return;
        }

        string sourceKey = "handover:" + accountId.ToString("N") + ":" + target.Id.ToString("N");
        if (_context.SourceKeys.Contains(sourceKey) || !_plannedKeys.Add(sourceKey))
        {
            return;
        }

        _work.Handovers.Add(new HandoverAdd(Guid.NewGuid(), accountId, Team(row[StagedFields.SourceTeam])?.Id, target.Id,
            Team(row[StagedFields.ConsumerTeam])?.Id, _input.SourceLabel, null, "Kaynak devir işareti: OK (" + _input.SourceLabel + ")", sourceKey, null, false));
    }

    private static readonly (string Field, string Label)[] _observationFields =
    [
        (StagedFields.PasswordLastSet, "Son parola değişikliği (gözlem)"),
        (StagedFields.LastLogonAdOrLdap, "AD/LDAP son oturum (gözlem)"),
        (StagedFields.LastLogonAd, "AD son oturum (gözlem)"),
        (StagedFields.Organization, "Organizasyon (gözlem)"),
        (StagedFields.GroupDirectorate, "Grup direktörlüğü (gözlem)"),
        (StagedFields.Comment, "Yorum (gözlem)"),
        (StagedFields.SourceTeam, "Kaynak ekip (gözlem)"),
        (StagedFields.ConsumerTeam, "Kullanan ekip (gözlem)"),
        (StagedFields.HandoverFlag, "Devir işareti (gözlem)")
    ];

    private ObservationAdd Observation(Guid accountId, string profile, string presence, StagedRow row)
    {
        string? comment = row[StagedFields.Comment];
        string[] texts = [.. new[] { StagedFields.PasswordLastSet, StagedFields.LastLogonAdOrLdap, StagedFields.LastLogonAd }
            .Where(f => row[f + "Text"] is not null).Select(f => row[f + "Text"]!)];
        if (texts.Length > 0)
        {
            comment = string.Join(" | ", texts.Prepend(comment).OfType<string>());
        }

        return new ObservationAdd(accountId, profile, presence, Stamp(row, StagedFields.PasswordLastSet), Stamp(row, StagedFields.LastLogonAdOrLdap),
            Stamp(row, StagedFields.LastLogonAd), row[StagedFields.Organization], row[StagedFields.GroupDirectorate], comment, row[StagedFields.SourceTeam],
            row[StagedFields.ConsumerTeam], row[StagedFields.HandoverFlag], row.RowNumber > 0 ? $"{_input.SourceLabel} / {row.Sheet} / satır {row.RowNumber}" : null,
            JsonSerializer.Serialize(row.Original));
    }

    /// <summary>
    /// "Not seen in this list" observations are inferred only from a declared complete list. The population is the
    /// declared organizations (children included) inside the importer's scope, narrowed to the declared domain when one
    /// is given. An observed row outside that population contradicts the declaration, so nothing is inferred; a partial
    /// or unknown list never implies absence. Absence stays an observation, never a closure.
    /// </summary>
    private IEnumerable<PlannedRow> PlanAbsences(int lastKey)
    {
        const string profile = ServiceAccountImportProfiles.CoordinationList;
        if (!_file.Rows.Any(r => r.Kind == StagedKinds.Observation && (r[StagedFields.ObservationProfile] ?? _input.Profile) == profile))
        {
            yield break;
        }

        if (_input.Coverage != ServiceAccountImportCoverage.Complete)
        {
            _warnings.Add("CoverageNotComplete:" + _input.Coverage);
            yield break;
        }

        HashSet<Guid> population = CoverageOrganizations();
        string? domain = ServiceAccountText.DomainKey(_input.DeclaredDomain);
        var created = _work.AccountCreates.ToDictionary(c => c.Id, c => (c.OrganizationId, c.DomainKey));
        bool Inside(Guid? organization, string? accountDomain) =>
            organization is { } id && population.Contains(id) && (domain is null || accountDomain == domain);
        bool InsideAccount(Guid id) => created.TryGetValue(id, out (Guid? Organization, string? Domain) c)
            ? Inside(c.Organization, c.Domain)
            : _byId.TryGetValue(id, out ContextAccount? account) && Inside(EffectiveOrganization(account), account.DomainKey);

        ContextAccount[] members = [.. _context.Accounts.Where(a => _context.Scope.Covers(a.Anchor) && Inside(EffectiveOrganization(a), a.DomainKey))];
        _coveragePopulation = members.Length + created.Count(c => Inside(c.Value.OrganizationId, c.Value.DomainKey));
        _coverageOutsideRows = _observed.Count(o => o.Item2 == profile && !InsideAccount(o.Item1));
        if (_coverageOutsideRows > 0)
        {
            _warnings.Add("CoverageContradicted:" + _coverageOutsideRows.ToString(System.Globalization.CultureInfo.InvariantCulture));
            yield break;
        }

        int key = lastKey;
        IEnumerable<(Guid Id, string Label, string Fingerprint)> candidates = members
            .Where(a => _context.LatestObservations.FirstOrDefault(o => o.AccountId == a.Id && o.Profile == profile) is not { SourceReportDate: { } d }
                || _input.SourceReportDate is not { } current || current >= d)
            .Select(a => (a.Id, Label(a), a.Id.ToString("N") + ":" + a.RowVersion))
            .Concat(_newAccounts.Values.Where(n => n.Created && created.TryGetValue(n.Id, out (Guid? Organization, string? Domain) c) && Inside(c.Organization, c.Domain))
                .Select(n => (n.Id, n.Name, "new:" + n.IdentityKey)))
            .OrderBy(a => a.Item2, StringComparer.Ordinal);
        foreach ((Guid id, string label, string fingerprint) in candidates)
        {
            if (_observed.Contains((id, profile)))
            {
                continue;
            }

            key++;
            StagedRow row = new("Bu partide görülmeyen", key, StagedKinds.Absence, new Dictionary<string, string?>(), new Dictionary<string, string?>
            {
                [StagedFields.Account] = label
            }, [], [], null, null);
            string decision = DecisionFor(key, ImportDecisions.Apply, _applyOrSkip);
            if (decision == ImportDecisions.Apply)
            {
                _work.Observations.Add(Observation(id, profile, "NotPresent", row));
            }

            AccountTarget target = new(id, label, null, null, fingerprint, null, [], false);
            yield return Row(key, row, ServiceAccountImportClasses.NotSeen, target,
                [new ImportFieldDiff("Kaynak listesinde görünüm", null, "Bu partide yok", "Gözlem (kapanış değil)")], [], _applyOrSkip, ImportDecisions.Apply, false,
                new { absence = true, _input.Coverage, population = _coveragePopulation });
        }
    }

    /// <summary>Declared organizations plus their descendants (cycles ignored).</summary>
    private HashSet<Guid> CoverageOrganizations()
    {
        HashSet<Guid> result = [];
        Queue<Guid> pending = new(_input.CoverageOrganizations ?? []);
        while (pending.Count > 0)
        {
            Guid next = pending.Dequeue();
            if (!result.Add(next))
            {
                continue;
            }

            foreach (ContextNamed child in _context.Organizations.Where(o => o.ParentId == next))
            {
                pending.Enqueue(child.Id);
            }
        }

        return result;
    }

    /// <summary>The account's report organization after this batch's additive fill (an empty organization may be filled).</summary>
    private Guid? EffectiveOrganization(ContextAccount account) =>
        account.OrganizationId ?? _work.AccountFills.FirstOrDefault(f => f.AccountId == account.Id)?.OrganizationId;
}
