using Microsoft.Extensions.Options;
using SecureOps.Domain.Announcements;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Announcements;

namespace SecureOps.Infrastructure.Announcements.Sources;

/// <summary>
/// Resolves the protected profile allowlist against server-owned configuration. A configured name
/// outside <see cref="MaintenanceProfiles.Allowed"/> is ignored, never promoted to a usable profile.
/// </summary>
public sealed class MaintenanceProfileCatalog(IOptions<AnnouncementSourceOptions> options)
{
    private AnnouncementSourceOptions Options => options.Value;

    /// <summary>Every allowlisted profile with its configuration state; never exposes collection IDs.</summary>
    public IReadOnlyList<MaintenanceProfileChoice> Choices() =>
        [.. MaintenanceProfiles.Allowed.Select(name =>
        {
            MaintenanceProfileState state = Resolve(name);
            return new MaintenanceProfileChoice(state.Name, state.Label, state.State, state.Missing);
        })];

    /// <summary>Configured profile, or null when the name is not allowlisted or not usable.</summary>
    public MaintenanceProfileOptions? Configured(string name) =>
        Resolve(name).State == "Configured" ? Options.Profiles[Match(name)!] : null;

    /// <summary>Evaluates one allowlisted name; unknown names are Invalid rather than silently absent.</summary>
    public MaintenanceProfileState Resolve(string name)
    {
        if (!MaintenanceProfiles.IsAllowed(name))
        { return new(name, name, "Invalid", ["Profile"]); }
        string? key = Match(name);
        if (key is null)
        { return new(name, name, "Unconfigured", ["Profile"]); }
        MaintenanceProfileOptions profile = Options.Profiles[key];
        List<string> missing = [];
        if (string.IsNullOrWhiteSpace(profile.CollectionId) || profile.CollectionId.Length > 64
            || !profile.CollectionId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        { missing.Add("CollectionId"); }
        foreach ((string field, string value) in new[] { ("Scope", profile.Scope), ("Impact", profile.Impact),
            ("Checks", profile.Checks), ("Description", profile.Description) })
        { if (string.IsNullOrWhiteSpace(value) || value.Length > 4000) { missing.Add(field); } }
        if (profile.To.Length == 0 || profile.To.Length > 50 || !profile.To.All(AnnouncementValidation.Address))
        { missing.Add("To"); }
        if (profile.Cc.Length > 50 || !profile.Cc.All(AnnouncementValidation.Address))
        { missing.Add("Cc"); }
        string label = string.IsNullOrWhiteSpace(profile.Label) || profile.Label.Length > 120
            || profile.Label.Any(char.IsControl) ? name : profile.Label;
        return new(name, label, missing.Count == 0 ? "Configured" : "Unconfigured", [.. missing]);
    }

    // Configuration keys are case-insensitive; the allowlist comparison itself stays exact.
    private string? Match(string name) =>
        Options.Profiles.Keys.FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One reconciled recipient field plus the operator decisions that produced it.</summary>
public sealed record ReconciledRecipients(RecipientDifference Difference, string[] Manual, string[] Removed);

/// <summary>
/// Reconciles profile defaults against the live draft. Operator intent is derived from the draft
/// itself relative to the last applied profile base, so manual additions and explicit removals
/// survive a profile change instead of being silently replaced by the new defaults.
/// </summary>
public static class RecipientReconciler
{
    private static readonly StringComparer _comparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Builds the difference for one field. <paramref name="previousBase"/> is the base list of the
    /// profile recorded in overrides; it is empty when no profile has ever been applied, which makes
    /// every current address a manual addition rather than a default to be overwritten.
    /// </summary>
    public static ReconciledRecipients Reconcile(string[] current, string[] nextBase,
        string[] previousBase, string[] storedManual, string[] storedRemoved)
    {
        string[] live = Normalize(current);
        string[] previous = Normalize(previousBase);
        HashSet<string> liveSet = new(live, _comparer);
        HashSet<string> previousSet = new(previous, _comparer);

        // Anything the operator holds that the last applied base did not supply is a manual addition.
        string[] manual = [.. live.Where(address => !previousSet.Contains(address))];
        // A removal stays recorded while the address is absent: a base list may stop mentioning it.
        string[] removed = [.. Normalize([.. storedRemoved, .. previous.Where(address => !liveSet.Contains(address))])
            .Where(address => !liveSet.Contains(address))];
        // storedManual only contributes addresses the operator still holds; deletions are honoured.
        manual = [.. Normalize([.. manual, .. storedManual.Where(address => liveSet.Contains(address))])];

        HashSet<string> removedSet = new(removed, _comparer);
        string[] proposed = [.. Normalize([.. nextBase, .. manual]).Where(address => !removedSet.Contains(address))];
        HashSet<string> proposedSet = new(proposed, _comparer);
        HashSet<string> baseSet = new(Normalize(nextBase), _comparer);
        return new(new RecipientDifference(
            live,
            proposed,
            [.. proposed.Where(address => !liveSet.Contains(address))],
            [.. live.Where(address => !proposedSet.Contains(address))],
            [.. manual.Where(proposedSet.Contains)],
            [.. removed.Where(baseSet.Contains)]), manual, removed);
    }

    /// <summary>Applies the reconciled result to a content record while keeping To/Cc disjoint.</summary>
    public static AnnouncementContent WithRecipients(AnnouncementContent content, string[] to, string[] cc)
    {
        string[] resolvedTo = Normalize(to);
        return content with { To = resolvedTo, Cc = [.. Normalize(cc).Where(address => !resolvedTo.Contains(address, _comparer))] };
    }

    // Order-preserving, case-insensitive deduplication; blank entries are never recipients.
    private static string[] Normalize(IEnumerable<string?> values) =>
        [.. values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()).Distinct(_comparer)];
}
