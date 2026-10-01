namespace SecureOps.Domain.Resources;

/// <summary>Bounded personal presentation settings; shortcut keys never confer access.</summary>
public sealed record ResourceWorkspaceLayout(string View, string Density, int PageSize, IReadOnlyList<string> Shortcuts)
{
    /// <summary>Default layout for legacy preferences and explicit layout reset.</summary>
    public static ResourceWorkspaceLayout Default => new("cards", "comfortable", 25, ["links", "groups"]);
}
