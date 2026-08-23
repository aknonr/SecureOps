using SecureOps.Shared.Contracts.Directory;

namespace SecureOps.Ui.Services;

/// <summary>
/// Presentation rules for Directory Explorer evidence.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is labelling and formatting of what the API returned. Nothing traverses,
/// combines, or infers: the direct and transitive sets stay as the server built them, a membership
/// verdict is the server's verdict, and an unknown attribute stays unknown rather than being
/// defaulted to a convenient value.
/// </para>
/// <para>
/// Two rules matter more than the rest, because getting either wrong turns operational evidence into
/// a false authorization answer:
/// </para>
/// <list type="bullet">
/// <item>A bounded traversal is not a negative result. "Not a member" may only be shown when the
/// server reports that nothing limited or truncated the walk.</item>
/// <item>Directory evidence is not a classification. The API reports what the objects say; it does
/// not decide that an account is a service account, and neither may the UI.</item>
/// </list>
/// </remarks>
public static class DirectoryView
{
    /// <summary>Shown wherever the directory returned no value for an attribute.</summary>
    public const string UnknownText = "Bilinmiyor";

    /// <summary>
    /// The best available human name for a group.
    /// </summary>
    /// <param name="group">Group summary.</param>
    /// <returns>Name, account name, distinguished name, or an explicit unknown.</returns>
    /// <remarks>
    /// Every naming field on the contract is nullable, so a group can legitimately arrive with only
    /// one of them populated. Falling through in this order keeps a row identifiable instead of
    /// rendering an empty cell.
    /// </remarks>
    public static string GroupName(DirectoryGroupSummaryDto group) =>
        FirstNonEmpty(group.Name, group.SamAccountName, group.DistinguishedName) ?? UnknownText;

    /// <summary>
    /// The best available human name for a group detail record.
    /// </summary>
    /// <param name="group">Group detail.</param>
    /// <returns>Name, account name, distinguished name, or an explicit unknown.</returns>
    public static string GroupName(DirectoryGroupDetailDto group) =>
        FirstNonEmpty(group.Name, group.SamAccountName, group.DistinguishedName) ?? UnknownText;

    /// <summary>
    /// The best available human name for a member.
    /// </summary>
    /// <param name="member">Member projection.</param>
    /// <returns>Name, account name, distinguished name, or an explicit unknown.</returns>
    public static string MemberName(DirectoryMemberDto member) =>
        FirstNonEmpty(member.Name, member.SamAccountName, member.DistinguishedName) ?? UnknownText;

    /// <summary>
    /// Turkish label for a group category.
    /// </summary>
    /// <param name="category">Server category value.</param>
    /// <returns>Operator-facing label, or the raw value when unrecognised.</returns>
    public static string CategoryLabel(string category) => category switch
    {
        "Security" => "Güvenlik",
        "Distribution" => "Dağıtım",
        "Unknown" => UnknownText,
        _ => category
    };

    /// <summary>
    /// Turkish label for a group scope.
    /// </summary>
    /// <param name="scope">Server scope value.</param>
    /// <returns>Operator-facing label, or the raw value when unrecognised.</returns>
    public static string ScopeLabel(string scope) => scope switch
    {
        "Global" => "Global",
        "Universal" => "Evrensel",
        "DomainLocal" => "Etki alanı yerel",
        "Unknown" => UnknownText,
        _ => scope
    };

    /// <summary>
    /// Turkish label for a member type.
    /// </summary>
    /// <param name="memberType">Server member type value.</param>
    /// <returns>Operator-facing label, or the raw value when unrecognised.</returns>
    public static string MemberTypeLabel(string memberType) => memberType switch
    {
        "User" => "Kullanıcı",
        "Group" => "Grup",
        "Computer" => "Bilgisayar",
        "Other" => "Diğer",
        _ => memberType
    };

    /// <summary>
    /// Turkish label for the directory's account-type evidence.
    /// </summary>
    /// <param name="evidence">Server evidence value.</param>
    /// <returns>Operator-facing label, or the raw value when unrecognised.</returns>
    /// <remarks>
    /// Deliberately phrased as what the directory object <i>is</i>, never as a verdict about how the
    /// account is used. A managed service account is an object class and is stated as such; an
    /// ordinary user object is stated as an ordinary user object, whatever SPNs it happens to carry.
    /// </remarks>
    public static string AccountTypeEvidenceLabel(string evidence) => evidence switch
    {
        "GroupManagedServiceAccount" => "Grup yönetilen servis hesabı nesnesi (gMSA)",
        "ManagedServiceAccount" => "Yönetilen servis hesabı nesnesi (MSA)",
        "User" => "Standart kullanıcı nesnesi",
        _ => evidence
    };

    /// <summary>
    /// Formats a nullable boolean attribute.
    /// </summary>
    /// <param name="value">Attribute value, or <c>null</c> when the directory returned none.</param>
    /// <param name="whenTrue">Text for <c>true</c>.</param>
    /// <param name="whenFalse">Text for <c>false</c>.</param>
    /// <returns>The matching text, or the unknown marker.</returns>
    public static string Flag(bool? value, string whenTrue, string whenFalse) => value switch
    {
        true => whenTrue,
        false => whenFalse,
        null => UnknownText
    };

    /// <summary>
    /// Formats a nullable UTC timestamp.
    /// </summary>
    /// <param name="value">Timestamp, or <c>null</c> when the directory returned none.</param>
    /// <returns>Local-time text, or the unknown marker.</returns>
    public static string Timestamp(DateTimeOffset? value) =>
        value is { } moment ? moment.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : UnknownText;

    /// <summary>
    /// Formats a password age in days.
    /// </summary>
    /// <param name="days">Age in days, or <c>null</c> when unknown.</param>
    /// <returns>Day count, or the unknown marker.</returns>
    public static string Days(int? days) => days is { } value ? $"{value} gün" : UnknownText;

    /// <summary>
    /// Describes every bound that applied to a graph traversal.
    /// </summary>
    /// <param name="traversal">Traversal metadata from the API.</param>
    /// <returns>One sentence per bound that was actually reached; empty when the walk was complete.</returns>
    /// <remarks>
    /// Each entry corresponds to one flag the server set. They are listed rather than summarised
    /// because they mean different things operationally: a depth limit and a provider result limit
    /// call for different follow-up, and a detected cycle is a directory finding in its own right.
    /// </remarks>
    public static IReadOnlyList<string> TraversalLimits(DirectoryTraversalMetadataDto traversal)
    {
        List<string> limits = [];

        if (traversal.DepthLimitReached)
        {
            limits.Add("Derinlik sınırına ulaşıldı; daha derindeki iç içe gruplar taranmadı.");
        }

        if (traversal.NodeLimitReached)
        {
            limits.Add("Düğüm sınırına ulaşıldı; grafın tamamı gezilmedi.");
        }

        if (traversal.EdgeLimitReached)
        {
            limits.Add("Bağlantı sınırına ulaşıldı; grafın tamamı gezilmedi.");
        }

        if (traversal.ProviderResultLimitReached)
        {
            limits.Add("Dizin sağlayıcısı sonuç sınırına ulaşıldı; bazı üyelikler dönmemiş olabilir.");
        }

        if (traversal.CycleDetected)
        {
            limits.Add("Grup üyeliğinde döngü tespit edildi; döngü güvenli şekilde kesildi.");
        }

        return limits;
    }

    /// <summary>
    /// Whether a negative membership answer can be stated as a fact.
    /// </summary>
    /// <param name="response">Membership-path response.</param>
    /// <returns><c>true</c> when the server walked the graph without hitting any bound.</returns>
    /// <remarks>
    /// This is the guard that keeps a bounded search from being reported as an authorization answer.
    /// When it returns false the UI must say the traversal was incomplete — never "not a member" —
    /// because a truncated walk proves nothing about the paths it did not take.
    /// </remarks>
    public static bool NegativeIsConclusive(DirectoryMembershipPathResponse response) =>
        !response.IsMember
        && !response.Traversal.IsTruncated
        && !response.PathsTruncated
        && TraversalLimits(response.Traversal).Count == 0;

    /// <summary>
    /// Whether any privileged membership was found across the configured groups.
    /// </summary>
    /// <param name="response">Privileged membership evidence.</param>
    /// <returns><c>true</c> when at least one configured group matched directly or transitively.</returns>
    public static bool HasPrivilegedMembership(DirectoryPrivilegedMembershipResponse response) =>
        response.Groups.Any(group => group.Direct || group.Transitive);

    private static string? FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
}
