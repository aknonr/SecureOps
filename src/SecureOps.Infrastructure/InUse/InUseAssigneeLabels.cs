using SecureOps.Domain.Access;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

/// <summary>Saved trusted profile labels only; no directory calls or identity resolution.</summary>
public static class InUseAssigneeLabels
{
    /// <summary>Disambiguates duplicate/missing names without changing immutable user IDs.</summary>
    public static IReadOnlyDictionary<Guid, string> Create(IReadOnlyList<ApplicationUser> users)
    {
        string Name(ApplicationUser user) => InUsePersonLabel.Format(user.DisplayName, user.LoginName);
        var names = users.GroupBy(Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var prefixes = users.GroupBy(u => u.Id.ToString("N")[..8]).ToDictionary(g => g.Key, g => g.Count());
        return users.ToDictionary(u => u.Id, u =>
        {
            string name = Name(u);
            if (names[name] == 1)
            { return name; }
            string suffix = u.Id.ToString("N")[..8];
            if (prefixes[suffix] > 1)
            { suffix = u.Id.ToString("D"); }
            return $"{name} · kullanıcı {suffix}";
        });
    }
}
