using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

/// <summary>Synthetic local adapter selected only with the existing Fake/Simulation source mode.</summary>
public sealed class LocalInUseSourceClient : IInUseSourceClient
{
    /// <inheritdoc />
    public Task<InUseBatch> DiscoverAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InUseEvidence unknown = new(null, "Synthetic fixture: unresolved relationship");
        InUseSource first = new("900001", "OR-DEMO-INUSE-01", "Application servers pending In Use review",
            new("sample.requester", "Synthetic requester display; not assignee"),
            unknown, new("Provisioning team A", "Synthetic provisioning evidence; not reviewer"),
            [Server("demo-server-01", "Production", "Sample service A"), Server("demo-server-02", "Test", "Sample service B")],
            "Synthetic fixture: two explicit service items with different services and environments.", true)
        {
            IdentityScope = "simulation:local:v1",
            ServiceItemsState = "Complete",
            AffectedAssetsState = "Complete",
            AffectedAssetCount = 0,
            Creator = new("sample.creator", "Synthetic technical creator; not requester")
        };
        InUseSource second = new("900002", "OR-DEMO-INUSE-02", "Related server evidence awaiting verification",
            unknown, unknown, unknown, [], "Synthetic fixture: zero resolved servers; relationship completeness unknown.", true);
        return Task.FromResult(new InUseBatch([first, second], true));
    }

    private static InUseServer Server(string id, string environment, string service) => new(id,
        new Dictionary<string, InUseEvidence>
        {
            ["HOSTNAME"] = new(id, "Synthetic server identity"),
            ["ENVANTER_ID"] = new(id, "Synthetic inventory identity"),
            ["SI_ENVIRONMENT"] = new(environment, "Synthetic per-server environment"),
            ["SERVICE NAME (ÜRÜN/UYGULAMA)"] = new(service, "Synthetic per-server service"),
            ["IP ADDRESS"] = new("192.0.2.10", "Synthetic documentation address"),
            ["Virtual PC User"] = new(id.EndsWith("01", StringComparison.Ordinal) ? "sample.pc-user" : null, "Synthetic UI-label evidence; business meaning unverified"),
            ["RFC Kaydı"] = new("OR-DEMO-OTHER", "Synthetic related request; not the current OR"),
            ["STATUS"] = new("IN_PROGRESS", "Synthetic service-item status")
        });
}

/// <summary>Fail-closed source when the integration is disabled; no fallback fixtures.</summary>
public sealed class DisabledInUseSourceClient : IInUseSourceClient
{
    /// <inheritdoc />
    public Task<InUseBatch> DiscoverAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("In Use source is disabled.");
}
