using System.Security.Cryptography;
using System.Text;

namespace SecureOps.Infrastructure.ItsmRequests;

/// <summary>
/// Deterministic synthetic provider for Development, Demo and Test (ADR-0004, ADR-0030). It makes no network call and never
/// stores anything. The draft's description selects the outcome so every state can be exercised:
/// <c>[fake:notsent]</c> NotSent/AuthenticationFailed (nothing dispatched); <c>[fake:unknown]</c> vendor <c>Success=false</c>
/// → OutcomeUnknown/Rejected; <c>[fake:noid]</c> <c>Success=true</c> without an inserted id → OutcomeUnknown/InvalidResponse;
/// <c>[fake:timeout]</c> throws <see cref="TimeoutException"/> after "dispatch"; <c>[fake:partial]</c> inserted but the number is
/// not read back → PartiallyCreated/NumberNotReadBack; <c>[fake:errortext]</c> <c>Success=true</c> + inserted id + filled error
/// text → Created (the vendor quirk); anything else Created. A missing requester location is NotSent like the real form.
/// The record number is a synthetic placeholder (<c>OR-1xxxxx</c>–<c>OR-8xxxxx</c>) derived from the draft; the source id is
/// always <c>900001</c>, the Fake tracking record. Results report Provider "Fake" and are never real evidence.
/// </summary>
public sealed class FakeItsmRequestCreator : IItsmRequestCreator
{
    /// <summary>Provider name reported in outcomes.</summary>
    public const string ProviderName = "Fake";

    /// <summary>Source id of every Fake insert (Found by the Fake tracking provider).</summary>
    public const string SourceId = "900001";

    /// <inheritdoc />
    public string Provider => ProviderName;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsSynthetic => true;

    /// <inheritdoc />
    public Task<ItsmRequestCreateOutcome> CreateAsync(ItsmRequestDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(draft.RequesterHomeLocationId))
        {
            return Task.FromResult(ItsmRequestCreateOutcome.NotSent(ItsmRequestCreateReason.RequesterLocationMissing, ProviderName));
        }

        bool Marked(string marker) => draft.Description.Contains(marker, StringComparison.OrdinalIgnoreCase);
        if (Marked("[fake:notsent]"))
        {
            return Task.FromResult(ItsmRequestCreateOutcome.NotSent(ItsmRequestCreateReason.AuthenticationFailed, ProviderName));
        }

        if (Marked("[fake:timeout]"))
        {
            throw new TimeoutException("Synthetic dispatch timeout.");
        }

        ItsmInsertResponse response = Marked("[fake:unknown]") ? new ItsmInsertResponse(false, null, true)
            : Marked("[fake:noid]") ? new ItsmInsertResponse(true, null, false)
            : new ItsmInsertResponse(true, SourceId, Marked("[fake:errortext]"));
        if (ItsmInsertRule.InsertedId(response) is not { } insertedId)
        {
            return Task.FromResult(ItsmRequestCreateOutcome.Unknown(ItsmInsertRule.UnprovenReason(response), ProviderName));
        }

        return Task.FromResult(Marked("[fake:partial]")
            ? new ItsmRequestCreateOutcome(ItsmRequestCreateStatus.PartiallyCreated, ItsmRequestCreateReason.NumberNotReadBack, insertedId, null, ProviderName)
            : new ItsmRequestCreateOutcome(ItsmRequestCreateStatus.Created, ItsmRequestCreateReason.None, insertedId, Number(draft), ProviderName));
    }

    /// <summary>Synthetic OR number in OR-100000..OR-899999 (never the Fake lookup's 000001-000003 placeholders).</summary>
    internal static string Number(ItsmRequestDraft draft)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', draft.ClientReference, draft.ShortDescription, draft.Description,
            draft.Environment, draft.RequesterUserId)));
        uint value = BitConverter.ToUInt32(hash, 0);
        return "OR-" + (100_000 + value % 800_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>Default provider: never creates anything. Callers check <see cref="IsEnabled"/> and refuse before writing.</summary>
public sealed class DisabledItsmRequestCreator : IItsmRequestCreator
{
    /// <summary>Provider name.</summary>
    public const string ProviderName = "Disabled";

    /// <inheritdoc />
    public string Provider => ProviderName;

    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public bool IsSynthetic => false;

    /// <inheritdoc />
    public Task<ItsmRequestCreateOutcome> CreateAsync(ItsmRequestDraft draft, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ItsmRequestCreateOutcome.NotSent(ItsmRequestCreateReason.NotConfigured, ProviderName));
    }
}
