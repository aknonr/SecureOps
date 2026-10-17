namespace SecureOps.Ui.Services;

/// <summary>
/// Raised when a SecureOps API call fails. Carries an already-translated <see cref="UiProblem"/> so
/// call sites render a consistent operator-facing state without re-deriving wording from status codes.
/// </summary>
/// <remarks>
/// <see cref="Exception.Message"/> is intentionally the safe title only. Diagnostic detail belongs in
/// server-side logs; nothing on this exception should be rendered raw.
/// </remarks>
/// <param name="problem">Translated operator-facing problem.</param>
/// <param name="innerException">Optional underlying transport exception.</param>
public sealed class SecureOpsApiException(UiProblem problem, Exception? innerException = null) : Exception(problem.Title, innerException)
{

    /// <summary>
    /// Operator-facing translation of the failure.
    /// </summary>
    public UiProblem Problem { get; } = problem;

    /// <summary>
    /// Raw JSON of the safe <c>current</c> extension an API error may carry (per-account results, the plan's current view).
    /// Null when the response had none; a page deserializes it into the contract type it expects.
    /// </summary>
    public string? CurrentJson { get; init; }
}
