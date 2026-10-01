using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>
/// Shared page behaviour: capability snapshot for gating (a courtesy — the API re-checks every call), one
/// in-flight command at a time, stale-response suppression and the single UiProblem failure path.
/// </summary>
public abstract class ServiceAccountPageBase : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _generation;

    /// <summary>Module API client.</summary>
    [Inject] protected ServiceAccountApiClient Api { get; set; } = default!;

    /// <summary>Access snapshot provider.</summary>
    [Inject] protected ICurrentAccessProvider AccessProvider { get; set; } = default!;

    /// <summary>Browser download bridge.</summary>
    [Inject] protected IJSRuntime Js { get; set; } = default!;

    /// <summary>Current access snapshot (null until resolved).</summary>
    protected AccessSnapshot? Access { get; private set; }

    /// <summary>True after the API rejected the session or capability; the page stops offering commands.</summary>
    protected bool AccessRejected { get; private set; }

    /// <summary>Last failure, rendered with SoProblemPanel.</summary>
    protected UiProblem? Problem { get; set; }

    /// <summary>A command or read is in flight.</summary>
    protected bool Busy { get; private set; }

    /// <summary>Cancellation for the page lifetime.</summary>
    protected CancellationToken Token => _lifetime.Token;

    /// <summary>Capability the page requires.</summary>
    protected abstract string RequiredCapability { get; }

    /// <summary>Whether the page may render its content.</summary>
    protected bool Allowed => !AccessRejected && Can(RequiredCapability);

    /// <summary>True when the access snapshot reports the capability.</summary>
    protected bool Can(string capability) => Access?.Can(capability) == true;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        AccessProvider.Changed += AccessChanged;
        Access = await AccessProvider.GetAsync(Token);
        if (Allowed)
        {
            await LoadAsync();
        }
    }

    /// <summary>Initial and refresh load.</summary>
    protected virtual Task LoadAsync() => Task.CompletedTask;

    /// <summary>
    /// Runs one command; returns false when it failed, was superseded or was not allowed. A command issued while another
    /// call runs is dropped (no double submit).
    /// </summary>
    protected async Task<bool> RunAsync(Func<CancellationToken, Task> action, bool clearProblem = true)
    {
        if (!Allowed || !_gate.Wait(0))
        {
            return false;
        }

        return await ExecuteAsync(action, clearProblem);
    }

    /// <summary>Runs a read after any call in flight instead of dropping it (loads and refreshes).</summary>
    protected async Task<bool> RunSerializedAsync(Func<CancellationToken, Task> action)
    {
        if (!Allowed)
        {
            return false;
        }

        try
        {
            await _gate.WaitAsync(Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }

        return Allowed ? await ExecuteAsync(action, true) : Release();
    }

    private bool Release()
    {
        _gate.Release();
        return false;
    }

    private async Task<bool> ExecuteAsync(Func<CancellationToken, Task> action, bool clearProblem)
    {
        Busy = true;
        if (clearProblem)
        {
            Problem = null;
        }

        int generation = _generation;
        try
        {
            await action(Token);
            return generation == _generation;
        }
        catch (SecureOpsApiException ex)
        {
            if (generation == _generation)
            {
                Problem = ex.Problem;
                if (ex.Problem.Kind is UiProblemKind.AccessDisabled or UiProblemKind.AccessPending or UiProblemKind.SessionExpired)
                {
                    AccessRejected = true;
                    ++_generation;
                }
            }

            return false;
        }
        catch (JSException)
        {
            Problem = new UiProblem(UiProblemKind.Unexpected, "ServiceAccountDownloadFailed", "Dosya tarayıcıya aktarılamadı",
                "Dosya sunucuda hazırlandı ancak indirilemedi.", ["Tekrar deneyin."], true, false, null, null, null);
            return false;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            Busy = false;
            _gate.Release();
        }
    }

    /// <summary>Hands a downloaded file to the browser.</summary>
    protected async Task SaveFileAsync(ServiceAccountFile file) =>
        await Js.InvokeVoidAsync("secureOpsDownload", Token, file.FileName, file.ContentType, Convert.ToBase64String(file.Content));

    /// <summary>Module context (scope label, reference lists).</summary>
    protected static string ScopeLabel(ServiceAccountMe? me) => me?.ScopeKind switch
    {
        "All" => "Tüm kurum",
        "Organization" => "Kurum/müdürlük: " + string.Join(", ", me.Organizations.Select(o => o.Label)),
        "Team" => "Ekip: " + string.Join(", ", me.Teams.Select(t => t.Label)),
        _ => "Kapsam tanımlı değil"
    };

    private void AccessChanged() => InvokeAsync(async () =>
    {
        ++_generation;
        Access = await AccessProvider.GetAsync(Token);
        AccessRejected = false;
        if (Allowed)
        {
            await LoadAsync();
        }

        StateHasChanged();
    });

    /// <inheritdoc />
    public void Dispose()
    {
        AccessProvider.Changed -= AccessChanged;
        ++_generation;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
