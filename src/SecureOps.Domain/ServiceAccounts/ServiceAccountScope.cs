namespace SecureOps.Domain.ServiceAccounts;

/// <summary>An active data-scope grant of one application user.</summary>
/// <param name="Kind">All, organization subtree or team.</param>
/// <param name="OrganizationId">Granted organization.</param>
/// <param name="TeamId">Granted team.</param>
public sealed record ScopeGrant(ScopeKind Kind, Guid? OrganizationId, Guid? TeamId);

/// <summary>Organization tree node.</summary>
/// <param name="Id">Organization.</param>
/// <param name="ParentId">Parent organization.</param>
public sealed record OrganizationNode(Guid Id, Guid? ParentId);

/// <summary>Team placement in the organization tree.</summary>
/// <param name="Id">Team.</param>
/// <param name="OrganizationId">Owning organization, if known.</param>
public sealed record TeamNode(Guid Id, Guid? OrganizationId);

/// <summary>Attributes that place an account in data scope.</summary>
/// <param name="ReportOrganizationId">Report organization.</param>
/// <param name="OwnerTeamId">Current confirmed owner team.</param>
/// <param name="OpenRequestTeamIds">Target teams of open requests.</param>
/// <param name="IncomingHandoverTeamIds">Target teams of proposed/accepted handovers.</param>
public sealed record AccountScopeAnchor(Guid? ReportOrganizationId, Guid? OwnerTeamId,
    IReadOnlyCollection<Guid> OpenRequestTeamIds, IReadOnlyCollection<Guid> IncomingHandoverTeamIds);

/// <summary>
/// Resolved data scope of one caller. Evaluated server-side for every query, detail, mutation,
/// export, evidence download and job; an empty scope sees nothing.
/// </summary>
public sealed class ServiceAccountScope
{
    private ServiceAccountScope(bool all, IReadOnlySet<Guid> organizations, IReadOnlySet<Guid> teams, IReadOnlySet<Guid> directTeams)
    {
        All = all;
        Organizations = organizations;
        Teams = teams;
        DirectTeams = directTeams;
    }

    /// <summary>Whole-module scope.</summary>
    public bool All { get; }

    /// <summary>Granted organizations including all descendants.</summary>
    public IReadOnlySet<Guid> Organizations { get; }

    /// <summary>Directly granted teams plus teams placed under granted organizations.</summary>
    public IReadOnlySet<Guid> Teams { get; }

    /// <summary>Only the directly granted teams ("my team").</summary>
    public IReadOnlySet<Guid> DirectTeams { get; }

    /// <summary>True when no data is visible.</summary>
    public bool IsEmpty => !All && Organizations.Count == 0 && Teams.Count == 0;

    /// <summary>True for organization-level (coordinator) scope: All or at least one organization.</summary>
    public bool HasOrganizationLevel => All || Organizations.Count > 0;

    /// <summary>A scope that sees nothing.</summary>
    public static ServiceAccountScope None { get; } = new(false, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    /// <summary>Resolves grants against the current organization/team tree (cycles are ignored safely).</summary>
    public static ServiceAccountScope Resolve(IEnumerable<ScopeGrant> grants, IEnumerable<OrganizationNode> organizations, IEnumerable<TeamNode> teams)
    {
        ScopeGrant[] active = [.. grants];
        if (active.Any(g => g.Kind == ScopeKind.All))
        {
            return new ServiceAccountScope(true, new HashSet<Guid>(), new HashSet<Guid>(),
                active.Where(g => g.TeamId is not null).Select(g => g.TeamId!.Value).ToHashSet());
        }

        OrganizationNode[] tree = [.. organizations];
        HashSet<Guid> orgs = [];
        Queue<Guid> pending = new(active.Where(g => g.Kind == ScopeKind.Organization && g.OrganizationId is not null).Select(g => g.OrganizationId!.Value));
        while (pending.Count > 0)
        {
            Guid next = pending.Dequeue();
            if (!orgs.Add(next))
            {
                continue;
            }

            foreach (OrganizationNode child in tree.Where(o => o.ParentId == next))
            {
                pending.Enqueue(child.Id);
            }
        }

        var direct = active.Where(g => g.Kind == ScopeKind.Team && g.TeamId is not null).Select(g => g.TeamId!.Value).ToHashSet();
        HashSet<Guid> allTeams = [.. direct, .. teams.Where(t => t.OrganizationId is { } o && orgs.Contains(o)).Select(t => t.Id)];
        return new ServiceAccountScope(false, orgs, allTeams, direct);
    }

    /// <summary>Whether the account is visible and workable for this caller.</summary>
    public bool Covers(AccountScopeAnchor account) =>
        All
        || account.ReportOrganizationId is { } org && Organizations.Contains(org)
        || account.OwnerTeamId is { } owner && Teams.Contains(owner)
        || account.OpenRequestTeamIds.Any(Teams.Contains)
        || account.IncomingHandoverTeamIds.Any(Teams.Contains);

    /// <summary>Whether the caller has organization-level authority over the account (owner-team changes, conflicts).</summary>
    public bool CoversAtOrganizationLevel(AccountScopeAnchor account) =>
        All || account.ReportOrganizationId is { } org && Organizations.Contains(org);

    /// <summary>Whether a team is inside the caller's scope.</summary>
    public bool CoversTeam(Guid? teamId) => All || teamId is { } id && Teams.Contains(id);
}
