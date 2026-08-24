using SecureOps.Shared.Contracts.Directory;

namespace SecureOps.Ui.Services;

/// <summary>
/// Presentation rules and Active Directory vocabulary for the Directory screens.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is labelling and formatting of what the API returned. Nothing traverses,
/// combines, or infers: the direct, primary, and transitive sets stay as the server built them, a
/// membership verdict is the server's verdict, and an unknown attribute stays unknown rather than
/// being defaulted to a convenient value.
/// </para>
/// <para>
/// The naming is deliberately operator-facing rather than schema-facing. An administrator reading
/// this screen thinks in terms of "AD Kullanıcı Adı", not <c>sAMAccountName</c>; raw attribute names
/// appear only in the technical detail sections where they are the point.
/// </para>
/// <para>
/// Three rules matter more than the rest, because getting any of them wrong turns operational
/// evidence into a false statement:
/// </para>
/// <list type="bullet">
/// <item>A bounded traversal is not a negative result. "Not a member" may only be shown when the
/// server reports that nothing limited or truncated the walk.</item>
/// <item>Directory evidence is not a classification. The API reports what the objects say; it does
/// not decide that an account is human, PAM, or a service account, and neither may the UI.</item>
/// <item>Absent evidence is not a false value. An attribute the directory did not return is
/// unknown, and a relationship the directory says does not exist is "tanımlı değil" — different
/// statements that must not be rendered the same way.</item>
/// </list>
/// </remarks>
public static class DirectoryView
{
    /// <summary>Shown when the directory returned no value for an attribute it might have.</summary>
    public const string UnknownText = "Bilinmiyor";

    /// <summary>Shown when the directory positively reports that nothing is configured.</summary>
    public const string NotDefinedText = "Tanımlı değil";

    // ---- operator-facing field names ---------------------------------------------------------
    // Collected here so the same Active Directory concept is never named two different things on
    // two different screens.

    /// <summary>Label for a user's display name.</summary>
    public const string UserNameLabel = "Ad Soyad";

    /// <summary>Label for a group's display name.</summary>
    public const string GroupNameLabel = "Grup Adı";

    /// <summary>Label for a user's <c>sAMAccountName</c>.</summary>
    public const string UserAccountLabel = "AD Kullanıcı Adı";

    /// <summary>Label for a group's <c>sAMAccountName</c>.</summary>
    public const string GroupAccountLabel = "AD Grup Hesap Adı";

    /// <summary>Label for <c>userPrincipalName</c>.</summary>
    public const string UpnLabel = "Kurumsal Oturum Adı (UPN)";

    /// <summary>Help text explaining what a UPN is.</summary>
    public const string UpnHelp = "Active Directory üzerinde hesabın kurumsal oturum kimliğidir.";

    /// <summary>Label for <c>groupType</c> category.</summary>
    public const string CategoryLabelName = "Grup Türü";

    /// <summary>Label for group scope.</summary>
    public const string ScopeLabelName = "Grup Kapsamı";

    /// <summary>Label for <c>managedBy</c>.</summary>
    public const string ManagedByLabel = "Grup Sorumlusu";

    /// <summary>Label for <c>distinguishedName</c>.</summary>
    public const string DistinguishedNameLabel = "AD Nesne Yolu";

    /// <summary>Label for the object class of a directory member.</summary>
    public const string ObjectTypeLabel = "AD Nesne Türü";

    /// <summary>Label for the set of groups a principal belongs to.</summary>
    public const string MemberOfLabel = "Üyesi Olduğu Gruplar";

    /// <summary>Help text explaining what a service principal name is.</summary>
    public const string SpnHelp =
        "Service Principal Name, bir servisin Active Directory üzerinde Kerberos ile tanınmasını "
        + "sağlayan kayıttır. Bir hesapta SPN bulunması, o hesabın bir servis tarafından "
        + "kullanıldığına dair göstergedir.";

    // ---- names -------------------------------------------------------------------------------

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
    /// Whether the account name adds anything to the display name already shown.
    /// </summary>
    /// <param name="name">Display name.</param>
    /// <param name="samAccountName">Account name.</param>
    /// <returns><c>true</c> when both exist and differ.</returns>
    /// <remarks>
    /// In many directories a group's name and <c>sAMAccountName</c> are identical. Printing the same
    /// string twice under two labels makes a dense table harder to scan and suggests a distinction
    /// that is not there.
    /// </remarks>
    public static bool ShowAccountNameSeparately(string? name, string? samAccountName) =>
        !string.IsNullOrWhiteSpace(name)
        && !string.IsNullOrWhiteSpace(samAccountName)
        && !string.Equals(name.Trim(), samAccountName.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The exact identifier to send when looking this group up again.
    /// </summary>
    /// <param name="group">Group summary.</param>
    /// <returns>Account name if present, otherwise the display name; <c>null</c> when neither is.</returns>
    /// <remarks>
    /// <c>sAMAccountName</c> resolves most reliably server-side, and the contract offers the display
    /// name as the only alternative. A distinguished name is deliberately never sent: the endpoints
    /// reject raw DNs.
    /// </remarks>
    public static string? ExactGroupIdentifier(DirectoryGroupSummaryDto group) =>
        FirstNonEmpty(group.SamAccountName, group.Name);

    /// <summary>
    /// The exact identifier to send when looking a member up again.
    /// </summary>
    /// <param name="member">Member projection.</param>
    /// <returns>Account name if present, otherwise the display name; <c>null</c> when neither is.</returns>
    public static string? ExactMemberIdentifier(DirectoryMemberDto member) =>
        FirstNonEmpty(member.SamAccountName, member.Name);

    // ---- vocabulary --------------------------------------------------------------------------

    /// <summary>
    /// Operator-facing name for a group category.
    /// </summary>
    /// <param name="category">Server category value.</param>
    /// <returns>Full descriptive label, or the raw value when unrecognised.</returns>
    /// <remarks>
    /// "Güvenlik" alone is ambiguous on screen — security of what? The full "Güvenlik Grubu" names
    /// the object, which is what the operator is looking at.
    /// </remarks>
    public static string CategoryLabel(string category) => category switch
    {
        "Security" => "Güvenlik Grubu",
        "Distribution" => "Dağıtım Grubu",
        "Unknown" => UnknownText,
        _ => category
    };

    /// <summary>
    /// One sentence on what a group category means operationally.
    /// </summary>
    /// <param name="category">Server category value.</param>
    /// <returns>Explanation, or <c>null</c> when the value is unrecognised.</returns>
    public static string? CategoryExplanation(string category) => category switch
    {
        "Security" => "Yetkilendirme ve erişim izinlerinde kullanılabilen Active Directory grubu.",
        "Distribution" => "Güvenlik yetkilendirmesi için kullanılmayan dağıtım grubudur.",
        _ => null
    };

    /// <summary>
    /// Group scope, shown as the directory reports it.
    /// </summary>
    /// <param name="scope">Server scope value.</param>
    /// <returns>The Active Directory scope name, or the raw value when unrecognised.</returns>
    /// <remarks>
    /// Deliberately untranslated. Global, Universal, and Domain Local are the names in every
    /// Microsoft tool and document an administrator will cross-reference; a Turkish rendering would
    /// be one more term to map back. The meaning goes in the help text instead.
    /// </remarks>
    public static string ScopeLabel(string scope) => scope switch
    {
        "Global" => "Global",
        "Universal" => "Universal",
        "DomainLocal" => "Domain Local",
        "Unknown" => UnknownText,
        _ => scope
    };

    /// <summary>
    /// One sentence on what a group scope means.
    /// </summary>
    /// <param name="scope">Server scope value.</param>
    /// <returns>Explanation, or <c>null</c> when the value is unrecognised.</returns>
    /// <remarks>
    /// Describes where the group can be used and what it can contain — the directory facts. It does
    /// not guess how this organisation actually uses the scope.
    /// </remarks>
    public static string? ScopeExplanation(string scope) => scope switch
    {
        "Global" => "Kendi etki alanındaki hesapları içerir ve ormandaki herhangi bir etki alanında "
                    + "izin vermek için kullanılabilir.",
        "Universal" => "Ormandaki herhangi bir etki alanından hesap ve grup içerebilir; genel katalogda tutulur.",
        "DomainLocal" => "Ormandaki herhangi bir etki alanından üye alabilir, ancak yalnızca kendi "
                         + "etki alanındaki kaynaklarda izin vermek için kullanılır.",
        _ => null
    };

    /// <summary>
    /// Operator-facing name for a directory object type.
    /// </summary>
    /// <param name="memberType">Server member type value.</param>
    /// <returns>Object type label, or the raw value when unrecognised.</returns>
    /// <remarks>
    /// Object type only. It says what the directory object is, never how the account is used: a
    /// human, a PAM account, and a service account are all <c>User</c> objects here.
    /// </remarks>
    public static string MemberTypeLabel(string memberType) => memberType switch
    {
        "User" => "Kullanıcı",
        "Group" => "AD Grubu",
        "Computer" => "Bilgisayar",
        "Other" => "Diğer",
        _ => memberType
    };

    /// <summary>
    /// Operator-facing name for how a principal holds a group.
    /// </summary>
    /// <param name="membershipKind">Server <c>membershipKind</c> value.</param>
    /// <returns>Membership label, or a neutral fallback when the server did not state a kind.</returns>
    /// <remarks>
    /// The three kinds are removed in three different places, which is the whole reason the contract
    /// keeps them apart. A direct membership is removed on the group. A primary membership is the
    /// account's <c>primaryGroupID</c> and cannot be removed at all without changing that attribute.
    /// A transitive membership comes through another group entirely.
    /// </remarks>
    public static string MembershipKindLabel(string? membershipKind) => membershipKind switch
    {
        "Direct" => "Doğrudan Üyelik",
        "Primary" => "Birincil Grup",
        "Transitive" => "Dolaylı / İç İçe Üyelik",
        _ => "Üyelik"
    };

    /// <summary>
    /// One sentence on what a membership kind means.
    /// </summary>
    /// <param name="membershipKind">Server <c>membershipKind</c> value.</param>
    /// <returns>Explanation, or <c>null</c> when the kind is unrecognised.</returns>
    public static string? MembershipKindExplanation(string? membershipKind) => membershipKind switch
    {
        "Direct" => "Hesap bu gruba doğrudan eklenmiş. Üyeliği kaldırmak için grubun kendisi düzenlenir.",
        "Primary" => "Hesabın birincil grubudur. Bu ilişki grup üyeliği listesinden kaldırılamaz; "
                     + "hesabın birincil grup tanımının değişmesi gerekir.",
        "Transitive" => "Hesap bu gruba başka bir grup üzerinden erişiyor. Üyelik zincirdeki ilgili "
                        + "grupta değiştirilir.",
        _ => null
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

    // ---- values ------------------------------------------------------------------------------

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
    /// Formats a nullable count.
    /// </summary>
    /// <param name="count">Count, or <c>null</c> when the evidence was unavailable.</param>
    /// <returns>The number, or the unknown marker.</returns>
    /// <remarks>
    /// Group counts became nullable when principal evidence and membership evidence were separated:
    /// a null here means the membership graph could not be read, not that the account holds no
    /// groups.
    /// </remarks>
    public static string Count(int? count) => count is { } value ? value.ToString() : UnknownText;

    // ---- traversal ---------------------------------------------------------------------------

    /// <summary>
    /// Describes every bound that applied to a graph traversal.
    /// </summary>
    /// <param name="traversal">Traversal metadata from the API, or <c>null</c> when unavailable.</param>
    /// <returns>One sentence per bound that was actually reached; empty when the walk was complete.</returns>
    /// <remarks>
    /// Each entry corresponds to one flag the server set. They are listed rather than summarised
    /// because they mean different things operationally: a depth limit and a provider result limit
    /// call for different follow-up, and a detected cycle is a directory finding in its own right.
    /// </remarks>
    public static IReadOnlyList<string> TraversalLimits(DirectoryTraversalMetadataDto? traversal)
    {
        if (traversal is null)
        {
            return [];
        }

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
            limits.Add("Active Directory sonuç sınırına ulaşıldı; bazı üyelikler dönmemiş olabilir.");
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
    /// Whether a group analysis returned everything it was asked for.
    /// </summary>
    /// <param name="analysis">Group analysis response.</param>
    /// <returns><c>true</c> only when the server reports completeness and no bound was reached.</returns>
    public static bool AnalysisIsComplete(DirectoryGroupAnalysisResponse analysis) =>
        analysis.IsComplete
        && !analysis.DescendantTraversal.IsTruncated
        && TraversalLimits(analysis.DescendantTraversal).Count == 0;

    /// <summary>
    /// How a principal reaches the target group, when the server said so.
    /// </summary>
    /// <param name="response">Membership-path response.</param>
    /// <returns>The membership kind, or <c>null</c> when the evidence does not name one.</returns>
    /// <remarks>
    /// Read from the last group in each proven chain, which is the target itself. Primary membership
    /// is worth surfacing separately because it cannot be removed the way an explicit one can.
    /// </remarks>
    public static string? PathMembershipKind(DirectoryMembershipPathResponse response)
    {
        foreach (DirectoryMembershipPathDto path in response.Paths)
        {
            if (path.Groups.Count > 0 && path.Groups[^1].MembershipKind is { Length: > 0 } kind)
            {
                return kind;
            }
        }

        return null;
    }

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
