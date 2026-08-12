using MudBlazor;

namespace SecureOps.Ui.Services;

/// <summary>
/// Broad classes of failure the UI presents differently. The API's stable error <c>code</c> stays the
/// authoritative value for support and correlation; this enum only selects presentation and recovery
/// affordances so every screen reacts to a failure the same way.
/// </summary>
public enum UiProblemKind
{
    /// <summary>The browser session is no longer authenticated.</summary>
    SessionExpired,

    /// <summary>Authenticated, but application access is still awaiting approval.</summary>
    AccessPending,

    /// <summary>Authenticated, but application access has been disabled.</summary>
    AccessDisabled,

    /// <summary>Authenticated and approved, but lacking the required capability.</summary>
    Forbidden,

    /// <summary>The requested record does not exist or is not visible to the caller.</summary>
    NotFound,

    /// <summary>The submitted input was rejected.</summary>
    Validation,

    /// <summary>The caller exceeded the permitted request rate.</summary>
    RateLimited,

    /// <summary>State changed underneath the operation; authoritative state must be refreshed.</summary>
    Conflict,

    /// <summary>A dependency the operation needs is temporarily unavailable.</summary>
    UpstreamUnavailable,

    /// <summary>The API could not be reached at the transport layer.</summary>
    Network,

    /// <summary>The request exceeded the client timeout.</summary>
    Timeout,

    /// <summary>Anything not classified above.</summary>
    Unexpected
}

/// <summary>
/// A backend failure translated into something an operator can act on: plain-language explanation,
/// concrete next steps, and a support reference. Raw exception text, stack traces, upstream response
/// bodies, and API URLs never reach this type — those stay in server-side logs.
/// </summary>
/// <param name="Kind">Presentation class used to pick severity and recovery affordances.</param>
/// <param name="Code">Stable API error code, or a UI-assigned code for transport failures.</param>
/// <param name="Title">Short headline stating what failed.</param>
/// <param name="Explanation">One or two sentences of plain-language cause.</param>
/// <param name="NextSteps">Concrete actions the operator can take, in priority order.</param>
/// <param name="Retryable">Whether retrying the same operation can reasonably succeed.</param>
/// <param name="RequiresRefresh">Whether authoritative server state must be reloaded before retrying.</param>
/// <param name="CorrelationId">Support reference echoed from the API.</param>
/// <param name="Stage">Safe pipeline stage label echoed from the API.</param>
/// <param name="StatusCode">HTTP status when the failure came from a response.</param>
public sealed record UiProblem(
    UiProblemKind Kind,
    string Code,
    string Title,
    string Explanation,
    IReadOnlyList<string> NextSteps,
    bool Retryable,
    bool RequiresRefresh,
    string? CorrelationId,
    string? Stage,
    int? StatusCode)
{
    /// <summary>
    /// MudBlazor severity for this problem class. Conditions the operator can resolve, or that clear on
    /// their own, stay at Warning; states that need someone else to act are Error.
    /// </summary>
    public Severity Severity => Kind switch
    {
        UiProblemKind.AccessPending => Severity.Info,
        UiProblemKind.NotFound => Severity.Info,
        UiProblemKind.Validation => Severity.Warning,
        UiProblemKind.RateLimited => Severity.Warning,
        UiProblemKind.Conflict => Severity.Warning,
        UiProblemKind.UpstreamUnavailable => Severity.Warning,
        UiProblemKind.Network => Severity.Warning,
        UiProblemKind.Timeout => Severity.Warning,
        UiProblemKind.SessionExpired => Severity.Warning,
        _ => Severity.Error
    };

    /// <summary>
    /// Whether the operator should be sent back through sign-in to recover.
    /// </summary>
    public bool RequiresSignIn => Kind == UiProblemKind.SessionExpired;
}
