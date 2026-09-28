using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.Import;

/// <summary>Batch-level planning parameters (declared by the operator; never taken from upload time).</summary>
public sealed record ImportPlanInput(string Profile, DateOnly? SourceReportDate, string? DeclaredScope, string? DeclaredDomain, string? TargetTeam,
    string SourceLabel);

/// <summary>
/// Deterministic preview/commit planner. Preview and the in-transaction commit re-plan run the same code;
/// each row's fingerprint must match or the commit is refused as stale. Existing data is never overwritten:
/// blanks never erase, manual fields are kept, conflicts become decisions and absence is only an observation.
/// </summary>
public sealed partial class ImportPlanner
{
    private readonly ImportPlanInput _input;
    private readonly StagedFile _file;
    private readonly ImportContext _context;
    private readonly IReadOnlyDictionary<int, ImportRowDecision> _decisions;
    private readonly ImportWork _work = new();
    private readonly List<string> _warnings = [];
    private readonly Dictionary<string, ContextAccount> _byIdentity;
    private readonly ILookup<string, ContextAccount> _byName;
    private readonly ILookup<string, ContextAccount> _byCollapsed;
    private readonly Dictionary<Guid, ContextAccount> _byId;
    private readonly Dictionary<string, (Guid Id, bool New)> _teams = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Guid Id, bool New)> _orgs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(Guid Id, bool New)>> _people = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NewAccount> _newAccounts = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> _labels = [];
    private readonly HashSet<string> _plannedKeys = new(StringComparer.Ordinal);
    private readonly HashSet<(Guid, string)> _observed = [];
    private readonly HashSet<Guid> _gmsaPlanned = [];

    private sealed record NewAccount(Guid Id, string Name, string NameKey, string? Domain, string? DomainKey, string IdentityKey, int RowKey)
    {
        public bool Created { get; set; }
    }

    private ImportPlanner(ImportPlanInput input, StagedFile file, ImportContext context, IReadOnlyDictionary<int, ImportRowDecision> decisions)
    {
        _input = input;
        _file = file;
        _context = context;
        _decisions = decisions;
        _byIdentity = context.Accounts.ToDictionary(a => a.IdentityKey, StringComparer.Ordinal);
        _byName = context.Accounts.ToLookup(a => a.NameKey, StringComparer.Ordinal);
        _byCollapsed = context.Accounts.ToLookup(a => Collapse(a.NameKey), StringComparer.Ordinal);
        _byId = context.Accounts.ToDictionary(a => a.Id);
        foreach (ContextNamed team in context.Teams)
        {
            _teams[team.Key] = (team.Id, false);
        }

        foreach (ContextNamed org in context.Organizations)
        {
            _orgs[org.Key] = (org.Id, false);
        }

        foreach (ContextPerson person in context.People)
        {
            foreach (string key in person.AliasKeys.Append(person.Key).Distinct(StringComparer.Ordinal))
            {
                Person(key).Add((person.Id, false));
            }
        }
    }

    /// <summary>Plans the batch for preview (no decisions) or commit (with the persisted decisions).</summary>
    public static ImportPlanResult Plan(ImportPlanInput input, StagedFile file, ImportContext context, IReadOnlyDictionary<int, ImportRowDecision> decisions)
    {
        ImportPlanner planner = new(input, file, context, decisions);
        return planner.Run();
    }

    private ImportPlanResult Run()
    {
        ApplyPackageAliases();
        List<(int Key, StagedRow Row)> rows = [.. _file.Rows.Select((row, index) => (index + 1, row))];
        List<PlannedRow> planned = [];
        // Pass 1 establishes account identity (creation or match) so dependent rows can resolve in any order.
        foreach ((int key, StagedRow row) in rows.Where(r => r.Row.Kind is StagedKinds.Account or StagedKinds.Observation))
        {
            planned.Add(row.Kind == StagedKinds.Account ? PlanAccount(key, row) : PlanObservation(key, row));
        }

        foreach ((int key, StagedRow row) in rows.Where(r => r.Row.Kind is not (StagedKinds.Account or StagedKinds.Observation)))
        {
            planned.Add(row.Kind switch
            {
                StagedKinds.Ownership => PlanOwnership(key, row),
                StagedKinds.Request => PlanRequest(key, row),
                StagedKinds.Action => PlanAction(key, row),
                StagedKinds.Communication => PlanCommunication(key, row),
                StagedKinds.Finding => PlanFinding(key, row),
                StagedKinds.Handover => PlanHandover(key, row),
                _ => Invalid(key, row, null, "UnsupportedRow")
            });
        }

        planned.AddRange(PlanAbsences(rows.Count));
        planned.Sort((a, b) => a.RowKey.CompareTo(b.RowKey));
        foreach (PlannedRow row in planned)
        {
            string decision = Decision(row);
            if (row.Classification is ServiceAccountImportClasses.Invalid or ServiceAccountImportClasses.OutOfScope)
            {
                _work.Invalid++;
            }
            else if (decision == ImportDecisions.Skip)
            {
                _work.Skipped++;
            }
            else if (row.Classification == ServiceAccountImportClasses.Same)
            {
                _work.Unchanged++;
            }
        }

        return new ImportPlanResult(planned, Summary(planned), _work, _warnings);
    }

    /// <summary>Effective decision for a row: persisted when allowed, otherwise the default.</summary>
    public string Decision(PlannedRow row) =>
        _decisions.TryGetValue(row.RowKey, out ImportRowDecision? chosen) && row.AllowedDecisions.Contains(chosen.Decision, StringComparer.Ordinal)
            ? chosen.Decision : row.DefaultDecision;

    private string DecisionFor(int key, string defaultDecision, IReadOnlyList<string> allowed) =>
        _decisions.TryGetValue(key, out ImportRowDecision? chosen) && allowed.Contains(chosen.Decision, StringComparer.Ordinal)
            ? chosen.Decision : defaultDecision;

    private Guid? DecisionTarget(int key) => _decisions.TryGetValue(key, out ImportRowDecision? chosen) ? chosen.TargetId : null;

    private void ApplyPackageAliases()
    {
        foreach (string[] pair in _file.PersonAliases)
        {
            if (ServiceAccountText.LabelKey(pair[0]) is not { } aliasKey || ServiceAccountText.LabelKey(pair[1]) is not { } standardKey
                || aliasKey == standardKey)
            {
                continue;
            }

            (Guid Id, bool New)? person = ResolvePerson(pair[1], out _);
            if (person is { } p && !Person(aliasKey).Any(existing => existing.Id == p.Id))
            {
                Person(aliasKey).Add(p);
                _work.PersonAliases.Add((p.Id, ServiceAccountText.Clean(pair[0])!, aliasKey, "Göç paketi yazım standardizasyonu (" + _input.SourceLabel + ")"));
            }
        }
    }

    private List<(Guid Id, bool New)> Person(string key)
    {
        if (!_people.TryGetValue(key, out List<(Guid Id, bool New)>? list))
        {
            list = [];
            _people[key] = list;
        }

        return list;
    }

    private static string Collapse(string key)
    {
        StringBuilder builder = new(key.Length);
        foreach (char c in key)
        {
            if (builder.Length == 0 || builder[^1] != c)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string Hash(object value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant()[..32];

    private static string Text(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";

    private static DateOnly? Date(StagedRow row, string field) =>
        row[field] is { } value && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly d) ? d : null;

    private static DateTime? Stamp(StagedRow row, string field) =>
        row[field] is { } value && DateTime.TryParseExact(value, "yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Unspecified) : null;
}
