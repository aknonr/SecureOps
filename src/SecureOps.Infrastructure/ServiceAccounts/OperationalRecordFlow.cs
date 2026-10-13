using Microsoft.Extensions.Logging;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ItsmRecords;
using SecureOps.Infrastructure.ItsmRequests;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>The writes the OR submission flow needs (implemented by <see cref="SqlServiceAccountRepository"/>; in-memory in unit tests).</summary>
public interface IOperationalRecordStore
{
    /// <summary>The submission an idempotency key already holds on this account, or null.</summary>
    public Task<OperationalRecordSubmissionRecord?> OperationalRecordByKeyAsync(Guid accountId, Guid key, CancellationToken cancellationToken);

    /// <summary>Writes the Sending row (and its event, history, audit) before any remote call; refuses per the plan rules.</summary>
    public Task<SaResult<Guid>> ReserveOperationalRecordAsync(OperationalRecordReservation reservation, ChangePlanCaller caller, CancellationToken cancellationToken);

    /// <summary>Persists an outcome while the row is still Sending; false leaves the row as it is.</summary>
    public Task<bool> RecordOperationalRecordOutcomeAsync(Guid submissionId, OperationalRecordOutcomeWrite outcome, SaActor actor, CancellationToken cancellationToken);
}

/// <summary>The person who pressed the button, mapped to the ITSM (ADR-0032). Only <see cref="CanSend"/> may continue.</summary>
/// <param name="Status">Mapping outcome.</param>
/// <param name="Reason">Why the mapping is Unknown.</param>
/// <param name="UserId">ITSM user id when Matched.</param>
/// <param name="HomeLocationId">Home location id when Matched and filled.</param>
public sealed record OperationalRecordRequester(ItsmUserMatchStatus Status, ItsmRecordLookupReason Reason, string? UserId, string? HomeLocationId)
{
    /// <summary>Matched with a home location: the only state that may send (fail closed on everything else).</summary>
    public bool CanSend => Status == ItsmUserMatchStatus.Matched && !string.IsNullOrWhiteSpace(UserId) && !string.IsNullOrWhiteSpace(HomeLocationId);

    /// <summary>Stable refusal code, or null when <see cref="CanSend"/>.</summary>
    public string? BlockedReason => Status != ItsmUserMatchStatus.Matched || string.IsNullOrWhiteSpace(UserId) ? "requesterNotMatched"
        : string.IsNullOrWhiteSpace(HomeLocationId) ? "requesterLocationMissing" : null;

    /// <summary>Turkish explanation of the mapping result.</summary>
    public string Label => Status switch
    {
        ItsmUserMatchStatus.Matched when string.IsNullOrWhiteSpace(HomeLocationId) =>
            "Turuncu Hat kullanıcınız bulundu ama konumu boş; OR formu konum ister. OR açılamaz.",
        ItsmUserMatchStatus.Matched => "Turuncu Hat kullanıcınız birebir eşleşti (Bildiren ve etkilenen kullanıcı siz olacaksınız).",
        ItsmUserMatchStatus.NotMatched => "Turuncu Hat'ta sizinle birebir eşleşen kullanıcı yok. OR açılamaz.",
        ItsmUserMatchStatus.Ambiguous => "Turuncu Hat'ta birden fazla kullanıcı eşleşti veya sicil ile kullanıcı adı farklı kişileri gösteriyor. OR açılamaz.",
        _ => Reason == ItsmRecordLookupReason.NotConfigured
            ? "Turuncu Hat kullanıcı eşlemesi bu ortamda yapılandırılmamış. OR açılamaz."
            : "Turuncu Hat kullanıcı eşlemesi doğrulanamadı (bilinmiyor). OR açılamaz."
    };

    /// <summary>Maps the server-held identity of the signed-in user: registration number first, then the user name (ADR-0032 amendment).</summary>
    public static async Task<OperationalRecordRequester> ResolveAsync(IItsmUserMapping mapping, string? registrationNumber, string? userName,
        CancellationToken cancellationToken)
    {
        ItsmUserMatchResult result = await mapping.MatchAsync(registrationNumber, userName, cancellationToken);
        return new OperationalRecordRequester(result.Status, result.Reason, result.Status == ItsmUserMatchStatus.Matched ? result.User?.UserId : null,
            result.Status == ItsmUserMatchStatus.Matched ? result.User?.HomeLocationId : null);
    }
}

/// <summary>
/// Sends one OR exactly once (ADR-0030 §3). Order: an existing idempotency key answers first (same fingerprint = the stored
/// result, replayed, no call; another fingerprint = 409); otherwise the Sending row is reserved in its own transaction, the
/// provider is called once with a bound that ignores the browser's cancellation, and the outcome is persisted in a second
/// transaction. Nothing is ever retried: a thrown or timed-out call is OutcomeUnknown, and a failed outcome write leaves the row
/// Sending for reconciliation.
/// </summary>
public sealed class OperationalRecordFlow(IOperationalRecordStore store, IItsmRequestCreator creator, TimeSpan dispatchBound, ILogger logger)
{
    /// <summary>Runs the flow; the caller has already validated the preview, the fingerprint and the requester.</summary>
    public async Task<SaResult<(OperationalRecordSubmissionRecord Submission, bool Replayed)>> SubmitAsync(OperationalRecordReservation reservation,
        ItsmRequestDraft draft, ChangePlanCaller caller, CancellationToken cancellationToken)
    {
        if (await ReplayAsync(reservation, cancellationToken) is { } replay)
        {
            return replay;
        }

        SaResult<Guid> reserved = await store.ReserveOperationalRecordAsync(reservation, caller, cancellationToken);
        if (!reserved.IsSuccess)
        {
            // A parallel request with the same key won the reservation: answer like a replay.
            return reserved.Field == SqlServiceAccountRepository.SubmissionKeyExists && await ReplayAsync(reservation, cancellationToken) is { } raced
                ? raced
                : SaResult<(OperationalRecordSubmissionRecord, bool)>.Fail(reserved.ErrorCode!, reserved.Field, reserved.Current);
        }

        // From here on nothing depends on the browser: the row exists and its outcome must be recorded once.
        ItsmRequestCreateOutcome outcome = await DispatchAsync(draft);
        OperationalRecordOutcomeWrite write = Write(outcome);
        if (!await store.RecordOperationalRecordOutcomeAsync(reserved.Value, write, caller.Actor, CancellationToken.None))
        {
            logger.LogError("Service Accounts OR outcome was not recorded; the submission stays Sending for reconciliation. SubmissionId={SubmissionId} State={State} CorrelationId={CorrelationId}",
                reserved.Value, write.State, caller.Actor.CorrelationId);
        }

        OperationalRecordSubmissionRecord? stored = await store.OperationalRecordByKeyAsync(reservation.AccountId, reservation.IdempotencyKey, CancellationToken.None);
        return stored is null
            ? SaResult<(OperationalRecordSubmissionRecord, bool)>.Fail(SaErrors.Unavailable)
            : (stored, false);
    }

    /// <summary>Maps a provider outcome to the stored state; a NotSent from a provider that has no reason is still NotSent.</summary>
    internal static OperationalRecordOutcomeWrite Write(ItsmRequestCreateOutcome outcome) => outcome.Status switch
    {
        ItsmRequestCreateStatus.Created when outcome.SourceId is not null && outcome.RecordNumber is not null =>
            new(OperationalRecordState.Created, null, outcome.SourceId, outcome.RecordNumber),
        // A "Created" without its number is not proven complete; keep the record id and let the person link the number.
        ItsmRequestCreateStatus.Created or ItsmRequestCreateStatus.PartiallyCreated when outcome.SourceId is not null =>
            new(OperationalRecordState.PartiallyCreated, Code(outcome.Reason, ItsmRequestCreateReason.NumberNotReadBack), outcome.SourceId, null),
        ItsmRequestCreateStatus.NotSent => new(OperationalRecordState.NotSent, Code(outcome.Reason, ItsmRequestCreateReason.NotConfigured), null, null),
        _ => new(OperationalRecordState.OutcomeUnknown, Code(outcome.Reason, ItsmRequestCreateReason.InvalidResponse), null, null)
    };

    private async Task<SaResult<(OperationalRecordSubmissionRecord, bool)>?> ReplayAsync(OperationalRecordReservation reservation, CancellationToken cancellationToken)
    {
        if (await store.OperationalRecordByKeyAsync(reservation.AccountId, reservation.IdempotencyKey, cancellationToken) is not { } stored)
        {
            return null;
        }

        return stored.PlanId == reservation.PlanId && string.Equals(stored.Fingerprint, reservation.Fingerprint, StringComparison.Ordinal)
            ? (stored, true)
            : SaResult<(OperationalRecordSubmissionRecord, bool)>.Fail(SaErrors.IdempotencyKeyReused, "idempotencyKey");
    }

    private async Task<ItsmRequestCreateOutcome> DispatchAsync(ItsmRequestDraft draft)
    {
        using CancellationTokenSource bound = new(dispatchBound);
        try
        {
            return await creator.CreateAsync(draft, bound.Token);
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        {
            return ItsmRequestCreateOutcome.Unknown(ItsmRequestCreateReason.Timeout, creator.Provider);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return ItsmRequestCreateOutcome.Unknown(ItsmRequestCreateReason.Unavailable, creator.Provider);
        }
#pragma warning disable CA1031 // Any other failure after dispatch must become a recorded OutcomeUnknown, never a lost Sending row.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.LogError("Service Accounts OR dispatch failed. FailureType={FailureType}", exception.GetType().Name);
            return ItsmRequestCreateOutcome.Unknown(ItsmRequestCreateReason.DispatchInterrupted, creator.Provider);
        }
    }

    private static string Code(ItsmRequestCreateReason reason, ItsmRequestCreateReason fallback) =>
        (reason == ItsmRequestCreateReason.None ? fallback : reason).ToString();
}
