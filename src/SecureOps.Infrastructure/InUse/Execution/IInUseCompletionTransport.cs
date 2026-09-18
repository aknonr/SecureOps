using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse.Execution;

/// <summary>Worker-owned lease. Never accepted by an HTTP outcome endpoint.</summary>
public sealed record InUseExecutionLease(InUseExecutionIntent Intent, byte[] Artifact, Guid Token, long Revision, int Step,
    IReadOnlyList<InUseStepEvidence> Evidence);

/// <summary>Typed authoritative outcome. Acknowledged is never promoted to Verified by the executor.</summary>
public sealed record InUseRemoteResult(string Outcome, string Code, string? RemoteId = null);

/// <summary>Known steps only; no arbitrary script, endpoint, property or status from the browser.</summary>
public interface IInUseCompletionTransport
{
    /// <summary>Performs exactly one step. Uncertain writes must return Unknown and must not be retried internally.</summary>
    public Task<InUseRemoteResult> ExecuteAsync(string step, InUseExecutionLease execution, CancellationToken token);
}

/// <summary>Durable SQL boundary, abstracted only to verify executor crash/failure behavior in isolation.</summary>
public interface IInUseExecutionStore
{
    /// <summary>Claims one safe step, revalidating actor/source/configuration and fencing before remote I/O.</summary>
    public Task<InUseExecutionLease?> ClaimAsync(Guid id, string fingerprint, string executor, CancellationToken token);
    /// <summary>Only the current leased step can record its result. Never trusts caller-supplied human identity.</summary>
    public Task<bool> CompleteAsync(InUseExecutionLease lease, InUseRemoteResult result, string executor, CancellationToken token);
}
