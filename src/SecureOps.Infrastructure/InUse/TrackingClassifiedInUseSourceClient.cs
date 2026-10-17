using SecureOps.Infrastructure.ItsmRecords;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

/// <summary>
/// Fills the server-classified source evidence of each discovered record from the read-only WASAS classification
/// (TH-11, ADR-0032 amendment 2026-10-17): <c>WasasActivity</c> (Completed, Pending or NotApproved),
/// <c>Lifecycle</c> (Closed or Open) and <c>CurrentStage</c> (AssignedToWasas or NotAssignedToWasas). A class the
/// source could not prove is left null, never inferred, so an unreadable source never hides or closes a record.
/// It reads and never writes, selects no person field, and keeps the inner client's own evidence when the
/// classification is unknown. Records beyond the batch time budget are simply not classified.
/// </summary>
public sealed class TrackingClassifiedInUseSourceClient(IInUseSourceClient inner, IItsmWasasTracking tracking) : IInUseSourceClient
{
    /// <summary>Time budget of the classification of one batch; the rest stays unclassified.</summary>
    public static readonly TimeSpan BatchBudget = TimeSpan.FromSeconds(40);

    internal const string ActivitySource = "TuruncuHat BPM_Actvty: single WASAS-group review activity, completion time and configured approved status code";
    internal const string LifecycleSource = "TuruncuHat OR row: configured closed stage/status ids";
    internal const string AssignmentSource = "TuruncuHat OR row: assigned group against the configured WASAS group id";

    /// <inheritdoc />
    public async Task<InUseBatch> DiscoverAsync(CancellationToken cancellationToken)
    {
        InUseBatch batch = await inner.DiscoverAsync(cancellationToken);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(BatchBudget);
        List<InUseSource> records = new(batch.Records.Count);
        foreach (InUseSource source in batch.Records)
        {
            if (budget.IsCancellationRequested)
            {
                records.Add(source);
                continue;
            }

            try
            {
                records.Add(Apply(source, await tracking.ClassifyAsync(source.Id, budget.Token)));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                records.Add(source);
            }
        }

        return batch with { Records = records };
    }

    /// <inheritdoc />
    public Task<System.Text.Json.JsonElement> DiagnoseAsync(string sourceId, CancellationToken token) => inner.DiagnoseAsync(sourceId, token);

    /// <summary>Maps a classification onto the source evidence; unknown classes keep what the source already carried.</summary>
    public static InUseSource Apply(InUseSource source, ItsmWasasClassification classification)
    {
        InUseEvidence? activity = classification.Activity switch
        {
            ItsmWasasActivityClass.Completed => new("Completed", ActivitySource),
            ItsmWasasActivityClass.Pending => new("Pending", ActivitySource),
            ItsmWasasActivityClass.NotApproved => new("NotApproved", ActivitySource),
            _ => null
        };
        InUseEvidence? lifecycle = classification.Closure switch
        {
            ItsmOrClosureClass.Closed => new("Closed", LifecycleSource),
            ItsmOrClosureClass.Open => new("Open", LifecycleSource),
            _ => null
        };
        InUseEvidence? stage = classification.Assignment switch
        {
            ItsmAssignmentClass.AssignedToWasas => new("AssignedToWasas", AssignmentSource),
            ItsmAssignmentClass.NotAssignedToWasas => new(InUseActivity.NotAssignedToWasas, AssignmentSource),
            _ => null
        };
        return source with
        {
            WasasActivity = activity ?? source.WasasActivity,
            Lifecycle = lifecycle ?? source.Lifecycle,
            CurrentStage = stage ?? source.CurrentStage
        };
    }
}
