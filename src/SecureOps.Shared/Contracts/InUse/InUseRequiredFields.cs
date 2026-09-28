namespace SecureOps.Shared.Contracts.InUse;

/// <summary>Approved business proposal only; source property identity/value/concurrency contracts remain independent.</summary>
public static class InUseRequiredFields
{
    /// <summary>Any PROD requires PROD; only known non-production environments propose TEST.</summary>
    public static string? Environment(InUseSource source)
    {
        string[] values = source.Servers.Select(s => s.Fields.GetValueOrDefault("SI_ENVIRONMENT")?.Value?.Trim().ToUpperInvariant() ?? "").ToArray();
        if (values.Contains("PROD"))
        { return "PROD"; }
        return values.Length > 0 && values.All(v => v is "DEV" or "TEST" or "UAT" or "NONPROD" or "NON-PROD" or "POC" or "PREPROD") ? "TEST" : null;
    }
}
