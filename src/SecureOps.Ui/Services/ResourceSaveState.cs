namespace SecureOps.Ui.Services;

/// <summary>Guards resource dialogs against duplicate and stale/unknown-outcome submissions.</summary>
public sealed class ResourceSaveState
{
    /// <summary>True until the pending save finishes, including failures.</summary>
    public bool Saving { get; private set; }
    /// <summary>Last safe failure. The caller keeps its draft while this is displayed.</summary>
    public UiProblem? Problem { get; private set; }
    /// <summary>Only validation failures permit editing and resubmitting the same draft/version.</summary>
    public bool CanSubmit => !Saving && (Problem is null || Problem.Kind == UiProblemKind.Validation);

    /// <summary>Only a known version rejection may be cleared by an explicit successful reread.</summary>
    public async Task RefreshConflictAsync(Func<Task> refresh)
    {
        if (Saving || Problem?.StatusCode != 409)
        {
            return;
        }
        Saving = true;
        try
        { await refresh(); Problem = null; }
        catch (SecureOpsApiException exception) { Problem = exception.Problem; }
        finally { Saving = false; }
    }

    /// <summary>Returns true only for a confirmed successful save, never for a rejected duplicate click.</summary>
    public async Task<bool> TrySaveAsync(Func<Task<UiProblem?>> save)
    {
        if (!CanSubmit)
        {
            return false;
        }
        Saving = true;
        try
        {
            Problem = await save();
            return Problem is null;
        }
        catch (SecureOpsApiException exception)
        {
            Problem = exception.Problem;
            return false;
        }
        finally
        {
            Saving = false;
        }
    }
}
