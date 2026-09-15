using SecureOps.Shared.Auth;

namespace SecureOps.Ui.Auth;

/// <summary>
/// A capability presented for humans: what it lets you do, and which area of the product it belongs to.
/// </summary>
/// <param name="Capability">Stable capability identifier from the API contract.</param>
/// <param name="Group">Functional area used to group capabilities in the UI.</param>
/// <param name="Label">Short Turkish name.</param>
/// <param name="Description">One sentence describing what the holder can do.</param>
public sealed record CapabilityDescriptor(
    string Capability,
    string Group,
    string Label,
    string Description);

/// <summary>
/// Turkish presentation vocabulary for application roles and capabilities.
/// </summary>
/// <remarks>
/// Role and capability identifiers are contract values and are never translated for comparison — only
/// for display. Anything not described here still renders, using its raw identifier, so a capability
/// added by the backend appears in the UI immediately rather than silently vanishing from an access
/// review.
/// </remarks>
public static class AccessLabels
{
    /// <summary>Application role identifiers from the API contract.</summary>
    public static class Roles
    {
        /// <summary>Full administrative role.</summary>
        public const string Admin = "Admin";

        /// <summary>Team lead role.</summary>
        public const string Lead = "Lead";

        /// <summary>Shift operator role.</summary>
        public const string Operator = "Operator";

        /// <summary>Role permitted to publish Jira issues.</summary>
        public const string JiraPublisher = "JiraPublisher";

        /// <summary>Audit reviewer role.</summary>
        public const string Auditor = "Auditor";

        /// <summary>View-only role.</summary>
        public const string ReadOnly = "ReadOnly";
    }

    /// <summary>Capability group names used for grouped display.</summary>
    public static class Groups
    {
        /// <summary>Identity lookup capabilities.</summary>
        public const string Identity = "Kimlik sorgulama";

        /// <summary>Operational record and Jira workflow capabilities.</summary>
        public const string OperationalRecords = "Operasyonel kayıt ve Jira";

        /// <summary>Access administration capabilities.</summary>
        public const string AccessAdministration = "Erişim yönetimi";

        /// <summary>Audit and diagnostics capabilities.</summary>
        public const string Oversight = "Denetim ve tanılama";

        /// <summary>Anything without a known group.</summary>
        public const string Other = "Diğer";
    }

    private static readonly Dictionary<string, string> _roleLabels = new(StringComparer.Ordinal)
    {
        [Roles.Admin] = "Sistem Yöneticisi",
        [Roles.Lead] = "Takım Lideri",
        [Roles.Operator] = "Operasyon Uzmanı",
        [Roles.JiraPublisher] = "Jira İşlem Yetkilisi",
        [Roles.Auditor] = "Denetim Görüntüleyicisi",
        [Roles.ReadOnly] = "Sadece Görüntüleme",
        ["ResourceCurator"] = "Bağlantı Yöneticisi",
        ["InUseReviewer"] = "In Use İnceleyicisi",
        ["InUseCoordinator"] = "In Use Koordinatörü"
    };

    private static readonly Dictionary<string, string> _roleDescriptions = new(StringComparer.Ordinal)
    {
        [Roles.Admin] = "Erişim yönetimi, operasyonel akışlar, Jira işlemleri, raporlama ve tanılama dahil tüm yetkilere sahiptir.",
        [Roles.Lead] = "Kimlik ve ekip bilgilerini görüntüler; operasyonel kayıtları yönetir, Jira işlemlerini yürütür ve tanılama yapar.",
        [Roles.Operator] = "Ekip ve operasyonel kayıtları görüntüler; Jira önizlemesi hazırlar ancak Jira kaydı oluşturamaz.",
        [Roles.JiraPublisher] = "Operasyonel kayıtları görüntüler, Jira önizlemesi hazırlar, Jira kaydı oluşturur ve başarısız aktarımı yeniden dener.",
        [Roles.Auditor] = "Denetim, yönetim raporları ve operasyonel tanılama bilgilerini salt okunur olarak görüntüler.",
        [Roles.ReadOnly] = "Operasyonel kayıtları ve uygulama bağlantılarını görüntüler; kişisel bağlantı gruplarını düzenler.",
        ["ResourceCurator"] = "Paylaşılan uygulama bağlantılarını ve kategorilerini yönetir; hedef uygulamalarda yetki vermez.",
        ["InUseReviewer"] = "In Use kayıtlarını yerel olarak inceler ve Excel raporu hazırlar; inceleyici ataması zorunlu değildir, kaynak sistemde yazma yetkisi vermez.",
        ["InUseCoordinator"] = "In Use salt okunur keşfi, yerel atama, inceleme ve Excel hazırlığı; kaynak sistemde yazma yetkisi vermez."
    };

    private static readonly Dictionary<string, CapabilityDescriptor> _capabilities = new(StringComparer.Ordinal)
    {
        [Capabilities.AnnouncementDrafts] = new(Capabilities.AnnouncementDrafts, "Planlı duyurular", "Duyuru hazırlama", "Kendi kaynak önerilerini inceler, taslak ve değişmez hazırlık oluşturur; mail göndermez."),
        [Capabilities.ResourcesView] = new(Capabilities.ResourcesView, "Uygulama bağlantıları", "Bağlantıları kullanma", "Bağlantıları görüntüler; kendi favorilerini ve gruplarını düzenler."),
        [Capabilities.ResourcesManage] = new(Capabilities.ResourcesManage, "Uygulama bağlantıları", "Bağlantı kataloğunu yönetme", "Paylaşılan bağlantıları ve kategorileri düzenler."),
        [Capabilities.InUseView] = new(Capabilities.InUseView, "In Use", "Kayıtları görüntüleme", "Kayıtlı In Use verisini okur."),
        [Capabilities.InUseReview] = new(Capabilities.InUseReview, "In Use", "Yerel inceleme", "Yerel inceleme taslağı ve Excel hazırlığı."),
        [Capabilities.InUseAssign] = new(Capabilities.InUseAssign, "In Use", "İnceleyici atama", "Onaylı uygulama kimliğine yerel atama."),
        [Capabilities.InUseRefresh] = new(Capabilities.InUseRefresh, "In Use", "Salt okunur keşif", "Kategori 4241 / grup 68 kaynak okuması."),
        [Capabilities.IdentityLookup] = new(
            Capabilities.IdentityLookup, Groups.Identity,
            "Kimlik sorgulama",
            "Tek bir PAM veya AD hesabını inceleme amacıyla sorgulayabilir."),

        [Capabilities.TeamView] = new(
            Capabilities.TeamView, Groups.Identity,
            "Ekip görünümü",
            "Onaylı ekip bilgilerini görüntüleyebilir."),

        [Capabilities.OperationalRecordsView] = new(
            Capabilities.OperationalRecordsView, Groups.OperationalRecords,
            "Kayıtları görüntüleme",
            "Operasyonel kayıt listesini ve kayıt detaylarını görebilir."),

        [Capabilities.OperationalRecordsCreateJiraPreview] = new(
            Capabilities.OperationalRecordsCreateJiraPreview, Groups.OperationalRecords,
            "Jira önizleme",
            "Jira kaydı oluşturmadan önce eşleme önizlemesi üretebilir."),

        [Capabilities.OperationalRecordsCreateJira] = new(
            Capabilities.OperationalRecordsCreateJira, Groups.OperationalRecords,
            "Jira kaydı oluşturma",
            "Operasyonel kayıttan Jira kaydı oluşturabilir."),

        [Capabilities.OperationalRecordsRetry] = new(
            Capabilities.OperationalRecordsRetry, Groups.OperationalRecords,
            "İş akışı yeniden deneme",
            "Yarım kalmış Jira aktarımlarını yeniden deneyebilir."),

        [Capabilities.OperationalRecordsViewDiagnostics] = new(
            Capabilities.OperationalRecordsViewDiagnostics, Groups.OperationalRecords,
            "İş akışı tanılama",
            "Aktarım iş akışının teknik durumunu inceleyebilir."),

        [Capabilities.AccessAdministration] = new(
            Capabilities.AccessAdministration, Groups.AccessAdministration,
            "Erişim yönetimi",
            "Uygulama erişim taleplerini ve rollerini yönetebilir."),

        [Capabilities.AccessApproveRequests] = new(
            Capabilities.AccessApproveRequests, Groups.AccessAdministration,
            "Talep onaylama",
            "Bekleyen erişim taleplerini onaylayabilir veya reddedebilir."),

        [Capabilities.AccessAssignRoles] = new(
            Capabilities.AccessAssignRoles, Groups.AccessAdministration,
            "Rol atama",
            "Kullanıcıların uygulama rollerini değiştirebilir."),

        [Capabilities.AccessManageUsers] = new(
            Capabilities.AccessManageUsers, Groups.AccessAdministration,
            "Kullanıcı yönetimi",
            "Kullanıcı erişimini devre dışı bırakabilir."),

        [Capabilities.AccessViewAudit] = new(
            Capabilities.AccessViewAudit, Groups.Oversight,
            "Erişim denetimi",
            "Erişim kararlarına ait denetim kayıtlarını görüntüleyebilir."),

        [Capabilities.AuditView] = new(
            Capabilities.AuditView, Groups.Oversight,
            "Denetim kayıtları",
            "Operasyonel denetim kanıtlarını görüntüleyebilir."),

        [Capabilities.SystemDiagnostics] = new(
            Capabilities.SystemDiagnostics, Groups.Oversight,
            "Sistem tanılama",
            "Onaylı sistem tanılama bilgilerini görüntüleyebilir.")
    };

    /// <summary>
    /// Returns a Turkish label for a role identifier.
    /// </summary>
    /// <param name="role">Role identifier.</param>
    /// <returns>Display label, or the identifier when unknown.</returns>
    public static string RoleLabel(string role) =>
        _roleLabels.TryGetValue(role, out string? label) ? label : role;

    /// <summary>
    /// Returns a Turkish description for a role identifier.
    /// </summary>
    /// <param name="role">Role identifier.</param>
    /// <returns>Description, or <c>null</c> when unknown.</returns>
    public static string? RoleDescription(string role) =>
        _roleDescriptions.TryGetValue(role, out string? description) ? description : null;

    /// <summary>
    /// Returns a Turkish label for an application access status.
    /// </summary>
    /// <param name="status"><c>Pending</c>, <c>Approved</c>, or <c>Disabled</c>.</param>
    /// <returns>Display label, or the raw value when unrecognised.</returns>
    /// <remarks>
    /// An unrecognised value is shown verbatim rather than mapped to a guess, so a status the API
    /// adds later is visible to the operator instead of silently reading as something it is not.
    /// </remarks>
    public static string StatusLabel(string status) => status switch
    {
        "Approved" => "Onaylı",
        "Pending" => "Onay bekliyor",
        "Disabled" => "Kapalı",
        _ => status
    };

    /// <summary>
    /// Returns a Turkish label for an access request's decision status.
    /// </summary>
    /// <param name="status"><c>Pending</c>, <c>Approved</c>, or <c>Rejected</c>.</param>
    /// <returns>Display label, or the raw value when unrecognised.</returns>
    /// <remarks>
    /// Deliberately separate from <see cref="StatusLabel"/>. A request is approved or
    /// <i>rejected</i>; a user is approved or <i>disabled</i>. The two vocabularies overlap on
    /// "Approved" but mean different things, and collapsing them would let a rejected request read
    /// as a disabled account.
    /// </remarks>
    public static string RequestStatusLabel(string status) => status switch
    {
        "Approved" => "Onaylandı",
        "Pending" => "Bekliyor",
        "Rejected" => "Reddedildi",
        _ => status
    };

    /// <summary>
    /// Returns a descriptor for a capability identifier.
    /// </summary>
    /// <param name="capability">Capability identifier.</param>
    /// <returns>Known descriptor, or a generic one carrying the raw identifier.</returns>
    public static CapabilityDescriptor Describe(string capability) =>
        _capabilities.TryGetValue(capability, out CapabilityDescriptor? descriptor)
            ? descriptor
            : new CapabilityDescriptor(capability, Groups.Other, capability, "Bu yetki için açıklama tanımlı değil.");

    /// <summary>
    /// Groups capabilities for display, in a stable functional order.
    /// </summary>
    /// <param name="capabilities">Granted capability identifiers.</param>
    /// <returns>Groups of described capabilities.</returns>
    public static IReadOnlyList<IGrouping<string, CapabilityDescriptor>> Group(
        IEnumerable<string> capabilities)
    {
        string[] order =
        [
            Groups.Identity,
            Groups.OperationalRecords,
            Groups.AccessAdministration,
            Groups.Oversight,
            Groups.Other
        ];

        return capabilities
            .Distinct(StringComparer.Ordinal)
            .Select(Describe)
            .GroupBy(descriptor => descriptor.Group)
            .OrderBy(group => Array.IndexOf(order, group.Key))
            .ToArray();
    }
}
