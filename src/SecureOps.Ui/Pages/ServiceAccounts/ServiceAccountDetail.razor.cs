using Microsoft.AspNetCore.Components;
using SecureOps.Shared.Contracts.ServiceAccounts;
using SecureOps.Ui.Services;
using SecureOps.Ui.Services.ServiceAccounts;

namespace SecureOps.Ui.Pages.ServiceAccounts;

/// <summary>Account detail: every command goes to the API; the answer (or, on conflict, a fresh read) replaces the view.</summary>
public partial class ServiceAccountDetail
{
    private AccountDetail? _detail;
    private ServiceAccountMe? _me;
    private ScopeBootstrapState? _bootstrap;
    private IReadOnlyList<OrganizationView> _organizations = [];
    private bool _directoryRequested;
    private IReadOnlyList<TeamView> _teams = [];
    private int _revision;
    private int _tab;
    private Guid? _loadedId;

    /// <summary>Account id.</summary>
    [Parameter] public Guid Id { get; set; }

    /// <inheritdoc />
    protected override string RequiredCapability => ServiceAccountCapabilities.View;

    private string? Lead => _detail is null ? null
        : $"{_detail.Summary.Domain ?? "Domain bilinmiyor"} · {(_detail.Summary.IdentityState == "Confirmed" ? "kimlik teyitli" : "kimlik geçici")}";

    private string WorkTabText => $"İşler ({_detail?.Requests.Count(r => r.Status == "Open") ?? 0} açık)";

    private string MailTabText => $"Yazışmalar ({_detail?.Communications.Count ?? 0})";

    private string FindingsTabText => $"Bulgular ({_detail?.Findings.Count ?? 0})";

    private string EvidenceTabText => $"Kanıtlar ({_detail?.Evidence.Count ?? 0})";

    /// <summary>Usage tab title with the rule state so an against-rule account stands out without opening the tab.</summary>
    private string UsageTabText => _detail?.Rule is { } rule && rule.Conformance == "Unplanned"
        ? $"Kullanım ve kural ({(_detail.Usages ?? []).Count(u => !u.Removed)}) · kurala aykırı"
        : $"Kullanım ve kural ({(_detail?.Usages ?? []).Count(u => !u.Removed)})";

    /// <summary>Scan tab title with every attached scan and the matches still waiting for a decision (server totals, not the page).</summary>
    private string ScanTabText => _detail is not { UsageScans: not null, UsageScanTotal: > 0 } detail ? "Kullanım taraması"
        : detail.UsageScanPending > 0 ? $"Kullanım taraması ({detail.UsageScanTotal}) · {detail.UsageScanPending} karar bekliyor"
        : $"Kullanım taraması ({detail.UsageScanTotal})";

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (_loadedId is { } loaded && loaded != Id && Allowed)
        {
            _detail = null;
            await LoadAsync();
        }
    }

    /// <inheritdoc />
    /// <remarks>Scope is read first so a caller without any data scope gets an explanation instead of a not-found error.</remarks>
    protected override Task LoadAsync() => RunSerializedAsync(async token =>
    {
        _loadedId = Id;
        _me = await Api.GetAsync<ServiceAccountMe>("/me", token);
        if (!_me.HasScope)
        {
            _detail = null;
            _bootstrap = Can(ServiceAccountCapabilities.Administer) ? await Api.GetAsync<ScopeBootstrapState>("/scope-grants/bootstrap", token) : null;
            return;
        }

        _detail = await Api.GetAsync<AccountDetail>($"/accounts/{Id}", token);
        _organizations = await Api.GetAsync<IReadOnlyList<OrganizationView>>("/organizations", token);
        _teams = await Api.GetAsync<IReadOnlyList<TeamView>>("/teams", token);
    });

    private async Task ExecuteAsync(SaCommand command)
    {
        bool saved = await RunAsync(async token =>
        {
            if (command.ReturnsDetail)
            {
                _detail = await Api.SendAsync<AccountDetail>(command.Method, command.Path, command.Body, token);
            }
            else
            {
                await Api.SendAsync<System.Text.Json.JsonElement>(command.Method, command.Path, command.Body, token);
                _detail = await Api.GetAsync<AccountDetail>($"/accounts/{Id}", token);
            }
        });
        await AfterCommandAsync(saved);
    }

    private async Task SaveMailAsync(CreateCommunicationRequest request) =>
        await ExecuteAsync(new SaCommand(HttpMethod.Post, "/communications", request, ReturnsDetail: false));

    private async Task UploadAsync(SaEvidenceUpload upload)
    {
        bool saved = await RunAsync(async token => _detail = await Api.UploadAsync<AccountDetail>($"/accounts/{Id}/evidence", upload.FileName,
            upload.ContentType, upload.Content, new Dictionary<string, string?>
            {
                ["ownerType"] = upload.OwnerType,
                ["ownerId"] = upload.OwnerId.ToString("D"),
                ["label"] = upload.Label
            }, token));
        await AfterCommandAsync(saved);
    }

    /// <summary>Attaches a usage-scan file; the API validates the whole file before anything is stored.</summary>
    private async Task UploadScanAsync(SaUsageScanUpload upload)
    {
        bool saved = await RunAsync(async token => _detail = await Api.UploadAsync<AccountDetail>($"/accounts/{Id}/usage-scans", upload.FileName,
            "application/json", upload.Content, new Dictionary<string, string?>
            {
                ["runStatement"] = upload.RunStatement,
                ["requestId"] = upload.RequestId?.ToString("D")
            }, token));
        await AfterCommandAsync(saved);
    }

    /// <summary>Older scans (read only); a failure shows the problem and keeps the current page.</summary>
    private async Task<UsageScanPage?> LoadScanPageAsync(int page)
    {
        UsageScanPage? result = null;
        await RunAsync(async token => result = await Api.GetAsync<UsageScanPage>($"/accounts/{Id}/usage-scans?page={page}", token));
        return result;
    }

    /// <summary>One page of a scan's items (read only); a failure shows the problem and keeps the current page.</summary>
    private async Task<UsageScanItemPage?> LoadScanItemsAsync(Guid linkId, string role, bool pendingOnly, int page)
    {
        UsageScanItemPage? result = null;
        await RunAsync(async token => result = await Api.GetAsync<UsageScanItemPage>(
            $"/accounts/{Id}/usage-scans/{linkId}/items?role={role}&pending={(pendingOnly ? "true" : "false")}&page={page}", token));
        return result;
    }

    private Task DownloadAsync(Guid evidenceId) => RunAsync(async token => await SaveFileAsync(await Api.DownloadAsync($"/evidence/{evidenceId}", token)));

    /// <summary>Success clears section forms; a conflict reloads the authoritative view while keeping typed input.</summary>
    private async Task AfterCommandAsync(bool saved)
    {
        if (saved)
        {
            _revision++;
            return;
        }

        if (Problem is { Kind: UiProblemKind.Conflict })
        {
            UiProblem conflict = Problem;
            await RunAsync(async token => _detail = await Api.GetAsync<AccountDetail>($"/accounts/{Id}", token));
            Problem = conflict;
        }
    }
}
