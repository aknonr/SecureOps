namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>
/// The answer of one module call a section shows itself (the event flow): the value, or the server's problem, or neither. Neither
/// with <see cref="Sent"/> means the call left but no answer arrived (timeout, network, superseded): the outcome is unknown.
/// </summary>
/// <param name="Value">Server answer on success.</param>
/// <param name="Problem">Server or transport problem.</param>
/// <param name="CurrentJson">Raw <c>current</c> extension of the problem (for example the close readiness of a 422).</param>
/// <param name="Sent">False when the call was not sent because another call was in flight.</param>
public sealed record SaCallResult<T>(T? Value, UiProblem? Problem, string? CurrentJson, bool Sent)
{
    /// <summary>True when the server answered with a value.</summary>
    public bool Succeeded => Value is not null;

    /// <summary>True when no server answer is known: the request may or may not have been recorded.</summary>
    public bool OutcomeUnknown => Sent && Value is null && (Problem is null || Problem.Kind is UiProblemKind.Network or UiProblemKind.Timeout);
}
