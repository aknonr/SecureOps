using SecureOps.Domain.Resources;
using SecureOps.Shared.Contracts.Resources;

namespace SecureOps.Ui.Services;

/// <summary>
/// Presentation rules for the link catalogue and personal shift-start sets.
/// </summary>
/// <remarks>
/// Kept out of the pages so the parts that are easy to get wrong — what the version guard means,
/// what an operator may conclude when a saved reference disappears, what the client may reject
/// before the server does — are testable without a browser.
/// <para>
/// Nothing here decides authorization. The API re-checks every call, and these helpers only choose
/// what to render.
/// </para>
/// </remarks>
public static class ResourceView
{
    /// <summary>Documented ceiling on links in one personal set.</summary>
    public const int MaxLinksPerSet = 100;

    /// <summary>Documented ceiling on personal sets.</summary>
    public const int MaxSets = 20;

    /// <summary>Documented ceiling on favourite references.</summary>
    public const int MaxFavourites = 200;

    /// <summary>Page sizes offered for the catalogue list.</summary>
    public static readonly int[] PageSizes = [10, 25, 50, 100];

    /// <summary>
    /// Explains a saved set whose resolved list is shorter than what the operator saved.
    /// </summary>
    /// <remarks>
    /// The contract deliberately returns only currently openable links and no count of what was
    /// excluded, so the UI cannot say how many entries are missing or why. Saying "3 links were
    /// removed" would be invented, and saying "you lack permission" would leak the existence of a
    /// restricted entry. This states the one thing that is both true and useful.
    /// </remarks>
    public const string PartialSetNotice =
        "Bu gruptaki bazı bağlantılar şu anda kullanılamıyor. Arşivlenmiş, kaldırılmış veya "
        + "görüntüleme kapsamınız dışında olabilir. Açma düğmesi yalnızca yukarıda listelenen, şu anda açılabilen bağlantıları açar.";

    /// <summary>Shown when a resolved set has nothing openable at all.</summary>
    public const string EmptySetNotice =
        "Bu grupta şu anda açılabilecek bağlantı yok. Kayıtlı seçimleriniz korunur; bağlantılar "
        + "yeniden kullanılabilir olduğunda burada görünür.";

    /// <summary>
    /// Whether a link is currently one of the caller's favourites.
    /// </summary>
    /// <param name="preferences">Current personal state, or <see langword="null"/> before it loads.</param>
    /// <param name="linkId">Link identifier.</param>
    /// <returns><c>true</c> when the link is favourited.</returns>
    public static bool IsFavourite(ResourcePreferencesResponse? preferences, Guid linkId) =>
        preferences?.Favourites.Any(link => link.Id == linkId) == true;

    /// <summary>
    /// Whether a set already contains a link.
    /// </summary>
    /// <param name="set">Resolved set.</param>
    /// <param name="linkId">Link identifier.</param>
    /// <returns><c>true</c> when the link is present.</returns>
    public static bool Contains(ShiftSetResponse set, Guid linkId) =>
        set.Links.Any(link => link.Id == linkId);

    /// <summary>
    /// Builds the ordered link identifiers for a save, preserving opening order.
    /// </summary>
    /// <param name="set">Set whose current membership is being replaced.</param>
    /// <returns>Identifiers in opening order.</returns>
    /// <remarks>
    /// Array order <i>is</i> the opening order in the contract, so this must never sort or
    /// deduplicate silently — duplicates are rejected by the server on purpose, and quietly
    /// collapsing them here would hide an editing mistake rather than surface it.
    /// </remarks>
    public static IReadOnlyList<Guid> LinkIds(ShiftSetResponse set) =>
        [.. set.Links.Select(link => link.Id)];

    /// <summary>
    /// Moves a link within an ordered list.
    /// </summary>
    /// <param name="ids">Current order.</param>
    /// <param name="index">Index to move.</param>
    /// <param name="offset">Positions to move by; negative moves earlier.</param>
    /// <returns>The reordered list, or the original when the move would leave the bounds.</returns>
    public static IReadOnlyList<Guid> Move(IReadOnlyList<Guid> ids, int index, int offset)
    {
        int target = index + offset;

        if (index < 0 || index >= ids.Count || target < 0 || target >= ids.Count)
        {
            return ids;
        }

        List<Guid> reordered = [.. ids];
        (reordered[index], reordered[target]) = (reordered[target], reordered[index]);
        return reordered;
    }

    /// <summary>
    /// Validates a personal set before it is sent.
    /// </summary>
    /// <param name="name">Proposed name.</param>
    /// <param name="linkIds">Proposed membership in opening order.</param>
    /// <param name="existing">Other sets owned by the caller, used for the name-uniqueness rule.</param>
    /// <param name="editingId">Set being edited, excluded from the uniqueness check.</param>
    /// <returns>An operator-facing message, or <see langword="null"/> when the input is acceptable.</returns>
    /// <remarks>
    /// A client-side pre-check, not a substitute for the server's. It exists so the common mistakes
    /// are reported next to the field the operator is looking at instead of arriving as a 400 after
    /// a round trip. The server remains authoritative and can still reject what passes here.
    /// </remarks>
    public static string? ValidateSet(
        string? name,
        IReadOnlyList<Guid> linkIds,
        IReadOnlyList<ShiftSetResponse> existing,
        Guid? editingId)
    {
        string trimmed = (name ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return "Grup adı zorunludur.";
        }

        if (trimmed.Length > 80)
        {
            return "Grup adı en fazla 80 karakter olabilir.";
        }

        // Ordinal case-insensitive, matching the documented per-owner uniqueness rule.
        if (existing.Any(set => set.Id != editingId
            && string.Equals(set.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return "Bu adda bir grubunuz zaten var.";
        }

        if (linkIds.Count > MaxLinksPerSet)
        {
            return $"Bir grupta en fazla {MaxLinksPerSet} bağlantı bulunabilir.";
        }

        if (linkIds.Distinct().Count() != linkIds.Count)
        {
            return "Aynı bağlantı bir grupta birden fazla kez yer alamaz.";
        }

        return null;
    }

    /// <summary>
    /// Whether another set can be created.
    /// </summary>
    /// <param name="preferences">Current personal state.</param>
    /// <returns><c>true</c> when below the documented ceiling.</returns>
    public static bool CanCreateSet(ResourcePreferencesResponse? preferences) =>
        (preferences?.Sets.Count ?? 0) < MaxSets;

    /// <summary>
    /// Operator-facing summary of an environment and location pair.
    /// </summary>
    /// <param name="link">Catalogue link.</param>
    /// <returns>A combined label, or <see langword="null"/> when neither is recorded.</returns>
    /// <remarks>
    /// Both are optional free text with no implied identity or authorization meaning, so they are
    /// shown as context and never used to decide what an operator may do.
    /// </remarks>
    public static string? Placement(ResourceLink link)
    {
        string[] parts = [.. new[] { link.Environment, link.Location }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())];

        return parts.Length == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>
    /// The host an external link points at, for display next to its name.
    /// </summary>
    /// <param name="link">Catalogue link.</param>
    /// <returns>The host, or <see langword="null"/> when the stored URL cannot be parsed.</returns>
    /// <remarks>
    /// Shows where a click will actually go before the operator takes it. Parsing defensively
    /// because a stored value that no longer parses must degrade to showing nothing rather than
    /// throwing inside a list render.
    /// </remarks>
    public static string? Host(ResourceLink link) =>
        Uri.TryCreate(link.Url, UriKind.Absolute, out Uri? uri) ? uri.Host : null;

    /// <summary>Filters the already bounded owner projection, never downloading the catalogue.</summary>
    public static ResourcePage? FavouritePage(ResourcePreferencesResponse? preferences, ResourceQuery query)
    {
        if (preferences is null)
        {
            return null;
        }
        string search = query.Search?.Trim() ?? string.Empty;
        ResourceLink[] matching = [.. preferences.Favourites
            .Where(l => query.CategoryId is null || l.CategoryId == query.CategoryId)
            .Where(l => string.IsNullOrWhiteSpace(query.Environment) || string.Equals(l.Environment, query.Environment.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(l => search.Length == 0 || l.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || l.Purpose.Contains(search, StringComparison.OrdinalIgnoreCase) || l.Tags.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase)))];
        return new([.. matching.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)], query.Page, query.PageSize, matching.Length);
    }

    /// <summary>
    /// Saved members the operator could see in the group but that the latest resolution did not return.
    /// </summary>
    /// <param name="saved">The group as last read from the personal projection.</param>
    /// <param name="resolved">What the server says is openable right now, in opening order.</param>
    /// <returns>The visible saved links that will not open, in saved order.</returns>
    /// <remarks>
    /// Both lists come from the server, so naming these entries reveals nothing the operator was not
    /// already shown. Hidden members are absent from <paramref name="saved"/> and stay unnamed and
    /// uncounted; <see cref="PartialSetNotice"/> covers them without inventing a reason.
    /// </remarks>
    public static IReadOnlyList<ResourceLink> NotOpening(ShiftSetResponse saved, IReadOnlyList<ResourceLink> resolved)
    {
        HashSet<Guid> openable = [.. resolved.Select(link => link.Id)];
        return [.. saved.Links.Where(link => !openable.Contains(link.Id))];
    }

    /// <summary>The visible slice of a page, e.g. "26–32 / 32", computed from the server's own paging.</summary>
    /// <param name="page">Server page; its own <see cref="ResourcePage.PageSize"/> is authoritative.</param>
    /// <returns>An operator-facing range, or <see langword="null"/> for an empty page.</returns>
    public static string? Range(ResourcePage page)
    {
        if (page.Items.Count == 0)
        {
            return null;
        }
        int first = ((page.Page - 1) * page.PageSize) + 1;
        return $"{first}–{first + page.Items.Count - 1} / {page.Total}";
    }

    /// <summary>Number of pages for a total, never less than one so "Sayfa 1 / 1" reads correctly when empty.</summary>
    /// <param name="total">Server total.</param>
    /// <param name="pageSize">Page size used for the query.</param>
    /// <returns>Page count.</returns>
    public static int PageCount(int total, int pageSize) =>
        Math.Max(1, (int)Math.Ceiling(total / (double)Math.Max(1, pageSize)));

    /// <summary>Plain-language list of the filters that currently narrow the list.</summary>
    /// <param name="favouritesOnly">Whether only the caller's favourites are listed.</param>
    /// <param name="search">Search text.</param>
    /// <param name="category">Display name of the selected category, if any.</param>
    /// <param name="environment">Selected environment, if any.</param>
    /// <returns>Filter descriptions in screen order; empty when nothing narrows the list.</returns>
    public static IReadOnlyList<string> ActiveFilters(bool favouritesOnly, string? search, string? category, string? environment)
    {
        List<string> filters = [];
        if (favouritesOnly)
        {
            filters.Add("Yalnız favorilerim");
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            filters.Add($"Arama: “{search.Trim()}”");
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            filters.Add($"Kategori: {category}");
        }
        if (!string.IsNullOrWhiteSpace(environment))
        {
            filters.Add($"Ortam: {environment.Trim()}");
        }
        return filters;
    }

    /// <summary>Confirmation after links were saved to a personal group, worded for one or many links.</summary>
    /// <param name="links">Links that were submitted.</param>
    /// <param name="groupName">Name of the group the server confirmed.</param>
    /// <returns>An operator-facing confirmation.</returns>
    public static string SavedToGroupNotice(IReadOnlyList<ResourceLink> links, string groupName) =>
        links.Count == 1
            ? $"“{links[0].Name}” kişisel grubunuza kaydedildi: {groupName}."
            : $"{links.Count} bağlantı kişisel grubunuza kaydedildi: {groupName}.";

    /// <summary>Finds the group a confirmed save wrote to, by identifier or, for a new group, by its unique name.</summary>
    /// <param name="preferences">The refreshed personal projection returned by the save.</param>
    /// <param name="id">Existing group identifier, or <see langword="null"/> for a newly created group.</param>
    /// <param name="name">Submitted group name.</param>
    /// <returns>The confirmed group, or <see langword="null"/> when it cannot be identified.</returns>
    /// <remarks>Names are unique per owner (ordinal, case-insensitive), the same rule <see cref="ValidateSet"/> applies.</remarks>
    public static ShiftSetResponse? SavedGroup(ResourcePreferencesResponse preferences, Guid? id, string name) =>
        id is { } existing
            ? preferences.Sets.FirstOrDefault(set => set.Id == existing)
            : preferences.Sets.FirstOrDefault(set => string.Equals(set.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}
