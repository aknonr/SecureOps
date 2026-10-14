namespace SecureOps.Domain.ServiceAccounts;

/// <summary>
/// What a server-side usage collector reported for one planned server (USAGE-SCAN-JOB-CONTRACT.md, ADR-0024 R2-4). Only
/// <see cref="Ok"/> carries a document; every other value is "no information" and never "not used" (SPEC rule 15).
/// </summary>
public enum UsageCollectionStatus
{
    /// <summary>Tamam: the endpoint answered with one <c>service-account-usage-v1</c> document.</summary>
    Ok,
    /// <summary>Erişilemedi: no WinRM answer within the open timeout (name not resolved, port closed, host down).</summary>
    Unreachable,
    /// <summary>UçNoktaYok: WinRM answered, the constrained (JEA) endpoint is not registered on the server.</summary>
    NoEndpoint,
    /// <summary>YetkiYok: the endpoint exists, the read identity was refused (Kerberos or role mapping).</summary>
    Forbidden,
    /// <summary>ZamanAşımı: connected, no complete answer within the operation timeout.</summary>
    Timeout,
    /// <summary>Bozuk: connected, but the answer was not exactly one valid document (also set by the module when its parser refuses it).</summary>
    Malformed,
    /// <summary>Çalışmadı: set by the module only — the scan was cancelled or stopped before this server reported.</summary>
    NotRun
}
