using SecureOps.Shared.Contracts.Identity;

namespace SecureOps.Ui.Services;

/// <summary>
/// Short-lived reuse of the identity lookup result currently on screen.
/// </summary>
/// <remarks>
/// Repeated submits of an unchanged form — double-clicks, Enter pressed twice, returning to the page —
/// would otherwise spend a rate-limit slot and write another audit entry for a question already
/// answered on screen. Within <see cref="Lifetime"/> the displayed result is reused instead.
/// <para>
/// Correctness rules this deliberately follows:
/// </para>
/// <list type="bullet">
///   <item><description>Reuse requires an <em>identical</em> request. Purpose and event references are
///   part of the signature because each is recorded with the lookup: a different purpose is a
///   different audited action, not a repeat of the same one.</description></item>
///   <item><description>Only successful results are cached. Failures always re-issue, so a transient
///   outage is never held on screen as if it were the current state.</description></item>
///   <item><description>Reuse is always visible, and an explicit refresh always forces a real call.</description></item>
/// </list>
/// <para>
/// This is a UI affordance, not a correctness mechanism. The API keeps its own read-through cache and
/// in-flight coalescing; both remain authoritative.
/// </para>
/// </remarks>
public sealed class IdentityLookupResultCache
{
    /// <summary>
    /// How long a displayed result may be reused. Matches the API's identity cache TTL so the UI never
    /// claims a result is current for longer than the server would have served the same value.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    /// <summary>
    /// ASCII unit separator. It cannot occur in an account, purpose, GUID, or event reference, so
    /// joined fields cannot run together and produce a false signature match.
    /// </summary>
    private const string _signatureSeparator = "\u001F";

    private string? _signature;
    private IdentityLookupResponse? _response;

    /// <summary>
    /// When the cached result was produced, or <c>null</c> when nothing is cached.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// Attempts to reuse the cached result for an identical request.
    /// </summary>
    /// <param name="request">Request the operator just submitted.</param>
    /// <param name="now">Current time.</param>
    /// <param name="response">Cached response when reuse applies.</param>
    /// <returns><c>true</c> when the caller should skip the API call.</returns>
    public bool TryReuse(
        IdentityLookupRequest request,
        DateTimeOffset now,
        out IdentityLookupResponse? response)
    {
        response = null;

        if (_response is null || CompletedAt is null)
        {
            return false;
        }

        if (!string.Equals(_signature, BuildSignature(request), StringComparison.Ordinal))
        {
            return false;
        }

        if (now - CompletedAt.Value >= Lifetime)
        {
            return false;
        }

        response = _response;
        return true;
    }

    /// <summary>
    /// Stores a successful result for short-lived reuse.
    /// </summary>
    /// <param name="request">Request that produced the result.</param>
    /// <param name="response">Successful lookup response.</param>
    /// <param name="completedAt">When the call completed.</param>
    public void Store(
        IdentityLookupRequest request,
        IdentityLookupResponse response,
        DateTimeOffset completedAt)
    {
        _signature = BuildSignature(request);
        _response = response;
        CompletedAt = completedAt;
    }

    /// <summary>
    /// Drops any cached result so the next submit calls the API.
    /// </summary>
    public void Invalidate()
    {
        _signature = null;
        _response = null;
        CompletedAt = null;
    }

    /// <summary>
    /// Builds the identity of a request for reuse comparison.
    /// </summary>
    /// <param name="request">Lookup request.</param>
    /// <returns>Comparison signature.</returns>
    /// <remarks>
    /// The account is compared case-insensitively and with any domain prefix removed, matching server
    /// normalization, so <c>DOMAIN\User</c> and <c>user</c> are correctly treated as one question.
    /// </remarks>
    private static string BuildSignature(IdentityLookupRequest request)
    {
        string account = AccountInputRules
            .StripDomainPrefix(request.Account?.Trim() ?? string.Empty)
            .ToLowerInvariant();

        return string.Join(
            _signatureSeparator,
            account,
            request.Purpose?.Trim() ?? string.Empty,
            request.AlertId?.ToString() ?? string.Empty,
            request.TuruncuhatEvtId?.Trim() ?? string.Empty);
    }
}
