using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.Import;

public sealed partial class ImportPlanner
{
    /// <summary>Account resolution result for one row.</summary>
    private sealed record AccountTarget(Guid? Id, string? Label, ContextAccount? Existing, NewAccount? Stub, string Fingerprint, string? Error,
        IReadOnlyList<ImportCandidate> Candidates, bool OwnsCreation, string? BatchVariant = null)
    {
        /// <summary>True when creating this account needs an explicit decision (existing candidates or an in-batch spelling variant).</summary>
        public bool NeedsDecision => OwnsCreation && (Candidates.Count > 0 || BatchVariant is not null);
    }

    private string? DomainLabel(StagedRow row) => ServiceAccountText.Clean(row[StagedFields.Domain]) is { } domain && !domain.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)
        ? domain : ServiceAccountText.Clean(_input.DeclaredDomain);

    /// <summary>Identity for Account/Observation rows; may register a new account owned by this row.</summary>
    private AccountTarget IdentityFor(int key, StagedRow row)
    {
        string? name = ServiceAccountText.Clean(row[StagedFields.Account]);
        string? nameKey = ServiceAccountText.AccountKey(name);
        if (nameKey is null)
        {
            return new AccountTarget(null, null, null, null, "none", "AccountNameMissing", [], false);
        }

        string? domain = DomainLabel(row);
        string? domainKey = ServiceAccountText.DomainKey(domain);
        string identity = ServiceAccountText.IdentityKey(nameKey, domainKey);
        if (_byIdentity.TryGetValue(identity, out ContextAccount? existing))
        {
            return Existing(existing);
        }

        if (_newAccounts.TryGetValue(identity, out NewAccount? planned))
        {
            return planned.Created
                ? new AccountTarget(planned.Id, planned.Name, null, planned, "new:" + identity, null, [], false)
                : new AccountTarget(null, planned.Name, null, planned, "new:" + identity, "AccountSkippedInRow:" + planned.RowKey, [], false);
        }

        List<ImportCandidate> candidates = [.. _byName[nameKey].Where(a => a.DomainKey != domainKey)
            .Select(a => new ImportCandidate(a.Id, Label(a), a.DomainKey is null ? "Aynı ad, domain bilinmiyor" : "Aynı ad, farklı domain"))];
        candidates.AddRange(_byCollapsed[Collapse(nameKey)].Where(a => a.NameKey != nameKey)
            .Select(a => new ImportCandidate(a.Id, Label(a), "Tekrarlanan harf farkı; otomatik birleştirilmez")));
        IReadOnlyList<string> allowed = candidates.Count > 0 ? [ImportDecisions.Create, ImportDecisions.Link, ImportDecisions.Skip] : [ImportDecisions.Create, ImportDecisions.Skip];
        string decision = DecisionFor(key, ImportDecisions.Create, allowed);
        if (decision == ImportDecisions.Link && DecisionTarget(key) is { } target && candidates.Any(c => c.Id == target) && _byId.TryGetValue(target, out ContextAccount? linked))
        {
            _work.AccountAliases.Add(new AccountAliasAdd(linked.Id, name!, nameKey, domain));
            return Existing(linked) with { Candidates = candidates, OwnsCreation = true };
        }

        string? variant = _newAccounts.Values.Where(n => n.NameKey != nameKey && Collapse(n.NameKey) == Collapse(nameKey)).Select(n => n.Name).FirstOrDefault();
        NewAccount stub = new(Guid.NewGuid(), name!, nameKey, domain, domainKey, identity, key) { Created = decision == ImportDecisions.Create };
        _newAccounts[identity] = stub;
        _labels[stub.Id] = name!;
        return new AccountTarget(stub.Created ? stub.Id : null, name, null, stub, "new:" + identity,
            stub.Created ? null : "AccountSkipped", candidates, true, variant);
    }

    /// <summary>Resolution for dependent rows (never creates an account).</summary>
    private AccountTarget Find(string? name)
    {
        string? nameKey = ServiceAccountText.AccountKey(name);
        if (nameKey is null)
        {
            return new AccountTarget(null, null, null, null, "none", "AccountNameMissing", [], false);
        }

        string identity = ServiceAccountText.IdentityKey(nameKey, ServiceAccountText.DomainKey(_input.DeclaredDomain));
        if (_byIdentity.TryGetValue(identity, out ContextAccount? existing))
        {
            return Existing(existing);
        }

        if (_newAccounts.TryGetValue(identity, out NewAccount? planned))
        {
            return planned.Created
                ? new AccountTarget(planned.Id, planned.Name, null, planned, "new:" + identity, null, [], false)
                : new AccountTarget(null, planned.Name, null, planned, "new:" + identity, "AccountSkippedInRow:" + planned.RowKey, [], false);
        }

        return new AccountTarget(null, ServiceAccountText.Clean(name), null, null, "missing:" + identity, "AccountUnknown", [], false);
    }

    private AccountTarget Existing(ContextAccount account)
    {
        _labels[account.Id] = account.Name;
        string? scopeError = _context.Scope.Covers(account.Anchor) ? null : "OutOfScope";
        return new AccountTarget(account.Id, Label(account), account, null, account.Id.ToString("N") + ":" + account.RowVersion, scopeError, [], false);
    }

    /// <summary>Plan-independent key for fingerprints: existing ID, or the identity key of an account created in this batch.</summary>
    private string Stable(Guid id) => _byId.ContainsKey(id) ? id.ToString("N")
        : _newAccounts.Values.FirstOrDefault(n => n.Id == id) is { } created ? "new:" + created.IdentityKey : id.ToString("N");

    private static string Label(ContextAccount account) => account.DomainKey is null ? account.Name : account.DomainKey + "\\" + account.Name;

    /// <summary>Team by exact label key; placeholders are unknown (null). Unknown labels become provisional teams.</summary>
    private (Guid Id, bool New)? Team(string? label)
    {
        if (ServiceAccountText.IsPlaceholder(label))
        {
            return null;
        }

        string key = ServiceAccountText.LabelKey(label)!;
        if (!_teams.TryGetValue(key, out (Guid Id, bool New) team))
        {
            team = (Guid.NewGuid(), true);
            _teams[key] = team;
            _work.Teams.Add(new NewNamed(team.Id, ServiceAccountText.Clean(label)!, key, null));
        }

        return team;
    }

    /// <summary>Organization by exact label key; creating one requires whole-module scope.</summary>
    private (Guid Id, bool New)? Organization(string? label, out string? error)
    {
        error = null;
        if (ServiceAccountText.IsPlaceholder(label))
        {
            return null;
        }

        string key = ServiceAccountText.LabelKey(label)!;
        if (_orgs.TryGetValue(key, out (Guid Id, bool New) org))
        {
            return org;
        }

        if (!_context.Scope.All)
        {
            error = "NewOrganizationNeedsFullScope";
            return null;
        }

        org = (Guid.NewGuid(), true);
        _orgs[key] = org;
        _work.Organizations.Add(new NewNamed(org.Id, ServiceAccountText.Clean(label)!, key, null));
        return org;
    }

    /// <summary>Person by exact label/alias key. Several matches are ambiguous and never merged.</summary>
    private (Guid Id, bool New)? ResolvePerson(string? label, out IReadOnlyList<Guid> ambiguous)
    {
        ambiguous = [];
        if (ServiceAccountText.IsPlaceholder(label))
        {
            return null;
        }

        string key = ServiceAccountText.LabelKey(label)!;
        List<(Guid Id, bool New)> matches = [.. Person(key).DistinctBy(p => p.Id)];
        if (matches.Count > 1)
        {
            ambiguous = [.. matches.Select(m => m.Id)];
            return null;
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        // Accent-folded matches are only candidates: they route to a decision and never create a look-alike person silently.
        string candidateKey = ServiceAccountText.CandidateKey(label)!;
        Guid[] similar = [.. _people.Where(p => ServiceAccountText.CandidateKey(p.Key) == candidateKey).SelectMany(p => p.Value).Select(p => p.Id).Distinct()];
        if (similar.Length > 0)
        {
            ambiguous = similar;
            return null;
        }

        (Guid Id, bool New) created = (Guid.NewGuid(), true);
        Person(key).Add(created);
        _work.People.Add(new NewNamed(created.Id, ServiceAccountText.Clean(label)!, key, null));
        return created;
    }

    private Guid? PersonOrWarn(string? label, string field, List<string> warnings)
    {
        (Guid Id, bool New)? person = ResolvePerson(label, out IReadOnlyList<Guid> ambiguous);
        if (ambiguous.Count > 0)
        {
            warnings.Add("AmbiguousPersonLeftEmpty:" + field);
        }

        return person?.Id;
    }

    private string Fp((Guid Id, bool New)? entry, string? label) =>
        entry is null ? "-" : entry.Value.New ? "new:" + ServiceAccountText.LabelKey(label) : entry.Value.Id.ToString("N");

    private PlannedRow Invalid(int key, StagedRow row, string? label, params string[] errors) =>
        new(key, row, errors.Contains("OutOfScope") || errors.Contains("NewOrganizationNeedsFullScope")
                ? ServiceAccountImportClasses.OutOfScope : ServiceAccountImportClasses.Invalid,
            null, label, [], [.. row.Errors.Concat(errors).Distinct()], row.Warnings, [], false, ImportDecisions.Skip, [ImportDecisions.Skip],
            Hash(new { invalid = errors, row.Errors }));

    private void AddReference(ExternalRecordType type, string? number, string entity, Guid entityId, Guid? accountId)
    {
        if (ServiceAccountText.RecordNumber(number) is { Length: <= 64 } value)
        {
            _work.References.Add(new ReferenceAdd(type, value, entity, entityId, accountId));
        }
    }

    private static ExternalRecordType OtherType(string? number) =>
        ServiceAccountText.RecordNumber(number) is { } value && System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Z][A-Z0-9_]{1,15}-[0-9]{1,9}$",
            System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(50))
            ? ExternalRecordType.JIRA : ExternalRecordType.OTHER;
}
