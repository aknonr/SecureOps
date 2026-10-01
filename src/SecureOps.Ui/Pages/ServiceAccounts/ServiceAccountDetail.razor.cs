using Microsoft.AspNetCore.Components;
using SecureOps.Shared.Contracts.ServiceAccounts;
using SecureOps.Ui.Services;
using SecureOps.Ui.Services.ServiceAccounts;

namespace SecureOps.Ui.Pages.ServiceAccounts;

/// <summary>Account detail: every command goes to the API; the answer (or, on conflict, a fresh read) replaces the view.</summary>
public partial class ServiceAccountDetail
{
    private AccountDetail? _detail;
    private IReadOnlyList<OrganizationView> _organizations = [];
    private bool _directoryRequested;
    private IReadOnlyList<TeamView> _teams = [];
    private int _revision;
    private Guid? _loadedId;

    /// <summary>Account id.</summary>
    [Parameter] public Guid Id { get; set; }

    /// <inheritdoc />
    protected override string RequiredCapability => ServiceAccountCapabilities.View;

    private string? Lead => _detail is null ? null
        : $"{_detail.Summary.Domain ?? "Domain bilinmiyor"} · {(_detail.Summary.IdentityState == "Confirmed" ? "kimlik teyitli" : "kimlik geçici")}";

    /// <summary>Usage tab title with the rule state so an against-rule account stands out without opening the tab.</summary>
    private string UsageTabText => _detail?.Rule is { } rule && rule.Conformance == "Unplanned"
        ? $"Kullanım ve kural ({(_detail.Usages ?? []).Count(u => !u.Removed)}) · kurala aykırı"
        : $"Kullanım ve kural ({(_detail?.Usages ?? []).Count(u => !u.Removed)})";

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
    protected override Task LoadAsync() => RunSerializedAsync(async token =>
    {
        _loadedId = Id;
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
