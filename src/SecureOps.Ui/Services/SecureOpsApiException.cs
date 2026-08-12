namespace SecureOps.Ui.Services;

/// <summary>
/// Raised when a SecureOps API call fails. Carries an already-translated <see cref="UiProblem"/> so
/// call sites render a consistent operator-facing state without re-deriving wording from status codes.
/// </summary>
/// <remarks>
/// <see cref="Exception.Message"/> is intentionally the safe title only. Diagnostic detail belongs in
/// server-side logs; nothing on this exception should be rendered raw.
/// </remarks>
public sealed class SecureOpsApiException : Exception
{
    /// <summary>
    /// Initializes a new API exception.
    /// </summary>
    /// <param name="problem">Translated operator-facing problem.</param>
    /// <param name="innerException">Optional underlying transport exception.</param>
    public SecureOpsApiException(UiProblem problem, Exception? innerException = null)
        : base(problem.Title, innerException)
    {
        Problem = problem;
    }

    /// <summary>
    /// Operator-facing translation of the failure.
    /// </summary>
    public UiProblem Problem { get; }
}
