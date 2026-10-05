namespace SecureOps.Shared.Contracts.Sessions;

/// <summary>Safe response metadata for terminal application sessions.</summary>
public static class ApplicationSessionHeaders
{
    /// <summary>Signals that the current browser authentication session must end.</summary>
    public const string ReauthenticationRequired = "X-SecureOps-Session-Reauthentication";

    /// <summary>Value identifying a terminal session, without disclosing its handle.</summary>
    public const string Required = "required";
}
