using SecureOps.Shared.Contracts.ServiceAccounts;
using SecureOps.Ui.Services.ServiceAccounts;

namespace SecureOps.Ui.Pages.ServiceAccounts;

/// <summary>Module administration: scope grants and the organization/team/person dictionaries.</summary>
public partial class ServiceAccountAdmin
{
    private static readonly string[] _orgKinds = ["Directorate", "Department", "Group", "Other"];
    private IReadOnlyList<ScopeGrantView> _grants = [];
    private IReadOnlyList<OrganizationView> _organizations = [];
    private IReadOnlyList<TeamView> _teams = [];
    private ScopeGrantView? _revoke;
    private string? _revokeReason;
    private GrantForm _grant = new();
    private OrgForm _org = new();
    private TeamForm _team = new();
    private int _revision;

    /// <inheritdoc />
    protected override string RequiredCapability => ServiceAccountCapabilities.Administer;

    /// <inheritdoc />
    protected override Task LoadAsync() => RunSerializedAsync(ReloadAsync);

    private async Task ReloadAsync(CancellationToken token)
    {
        _grants = await Api.GetAsync<IReadOnlyList<ScopeGrantView>>("/scope-grants", token);
        _organizations = await Api.GetAsync<IReadOnlyList<OrganizationView>>("/organizations", token);
        _teams = await Api.GetAsync<IReadOnlyList<TeamView>>("/teams", token);
        _roles = await Api.GetAsync<IReadOnlyList<TeamRoleView>>("/team-roles", token);
    }

    private IReadOnlyList<TeamRoleView> _roles = [];
    private Guid? _roleTeam;
    private string _roleKind = ServiceAccountTeamRoles.SqlTeam;
    private string? _roleReason;
    private TeamRoleView? _roleRevoke;
    private string? _roleRevokeReason;

    private static string RoleLabel(string role) => role == ServiceAccountTeamRoles.GmsaExecutor ? "gMSA yürütücü ekip" : "SQL ekibi";

    private async Task AddRoleAsync()
    {
        if (await RunAsync(async token =>
        {
            await Api.SendAsync<Guid>(HttpMethod.Post, "/team-roles", new CreateTeamRoleRequest(_roleTeam!.Value, _roleKind, _roleReason!.Trim()), token);
            await ReloadAsync(token);
        }))
        {
            (_roleTeam, _roleReason) = (null, null);
        }
    }

    private async Task RevokeRoleAsync()
    {
        if (await RunAsync(async token =>
        {
            await Api.SendAsync<Guid>(HttpMethod.Post, $"/team-roles/{_roleRevoke!.Id}/revoke", new RevokeTeamRoleRequest(_roleRevokeReason!.Trim()), token);
            await ReloadAsync(token);
        }))
        {
            (_roleRevoke, _roleRevokeReason) = (null, null);
        }
    }

    private async Task GrantAsync()
    {
        if (await RunAsync(async token =>
        {
            await Api.SendAsync<Guid>(HttpMethod.Post, "/scope-grants", new CreateScopeGrantRequest(_grant.Identity!.Trim(), _grant.Kind,
                _grant.Kind == "Organization" ? _grant.OrganizationId : null, _grant.Kind == "Team" ? _grant.TeamId : null, _grant.Reason!.Trim()), token);
            await ReloadAsync(token);
        }))
        {
            _grant = new GrantForm();
        }
    }

    private async Task RevokeAsync()
    {
        if (await RunAsync(async token =>
        {
            await Api.SendAsync<Guid>(HttpMethod.Post, $"/scope-grants/{_revoke!.Id}/revoke", new RevokeScopeGrantRequest(_revoke.Version, _revokeReason!.Trim()), token);
            await ReloadAsync(token);
        }))
        {
            (_revoke, _revokeReason) = (null, null);
        }
    }

    private void EditOrg(OrganizationView org) => _org = new OrgForm { Id = org.Id, Name = org.Name, Kind = org.Kind, ParentId = org.ParentId, Version = org.Version };

    private void EditTeam(TeamView team) =>
        _team = new TeamForm { Id = team.Id, Name = team.Name, OrganizationId = team.OrganizationId, Provisional = team.Provisional, Version = team.Version };

    private async Task SaveOrgAsync()
    {
        SaveOrganizationRequest request = new(_org.Name!.Trim(), _org.Kind, _org.ParentId, _org.Version);
        if (await RunAsync(async token =>
        {
            await Api.SendAsync<Guid>(_org.Id is null ? HttpMethod.Post : HttpMethod.Put, _org.Id is null ? "/organizations" : $"/organizations/{_org.Id}", request, token);
            await ReloadAsync(token);
        }))
        {
            _org = new OrgForm();
        }
    }

    private async Task SaveTeamAsync()
    {
        SaveTeamRequest request = new(_team.Name!.Trim(), _team.OrganizationId, _team.Provisional, _team.Version);
        if (await RunAsync(async token =>
        {
            await Api.SendAsync<Guid>(_team.Id is null ? HttpMethod.Post : HttpMethod.Put, _team.Id is null ? "/teams" : $"/teams/{_team.Id}", request, token);
            await ReloadAsync(token);
        }))
        {
            _team = new TeamForm();
        }
    }

    private async Task PersonCommandAsync(SaCommand command)
    {
        if (await RunAsync(token => Api.SendAsync<Guid>(command.Method, command.Path, command.Body, token)))
        {
            _revision++;
        }
    }

    private static string ScopeText(ScopeGrantView grant) => grant.ScopeKind switch
    {
        "All" => "Tüm kurum",
        "Organization" => "Kurum: " + (grant.Organization?.Label ?? "?"),
        _ => "Ekip: " + (grant.Team?.Label ?? "?")
    };

    private static string OrgKind(string kind) => kind switch
    {
        "Directorate" => "Müdürlük",
        "Department" => "Daire",
        "Group" => "Grup",
        _ => "Diğer"
    };

    private sealed class GrantForm
    {
        public string? Identity { get; set; }
        public string Kind { get; set; } = "Organization";
        public Guid? OrganizationId { get; set; }
        public Guid? TeamId { get; set; }
        public string? Reason { get; set; }
    }

    private sealed class OrgForm
    {
        public Guid? Id { get; set; }
        public string? Name { get; set; }
        public string Kind { get; set; } = "Directorate";
        public Guid? ParentId { get; set; }
        public string? Version { get; set; }
    }

    private sealed class TeamForm
    {
        public Guid? Id { get; set; }
        public string? Name { get; set; }
        public Guid? OrganizationId { get; set; }
        public bool Provisional { get; set; }
        public string? Version { get; set; }
    }
}
