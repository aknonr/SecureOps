using System.Security.Cryptography;
using System.Text;

namespace SecureOps.Domain.ServiceAccounts;

/// <summary>State of one "open OR" attempt for one plan account (migration 035, ADR-0030 §3, PROPOSED).</summary>
public enum OperationalRecordState
{
    /// <summary>Row written, insert sent or about to be sent, no persisted outcome (also what a crash leaves).</summary>
    Sending,
    /// <summary>Refused before the insert was dispatched; a new explicit attempt is allowed.</summary>
    NotSent,
    /// <summary>Inserted and the OR number read back.</summary>
    Created,
    /// <summary>Inserted (internal id known) but the number or a property could not be confirmed.</summary>
    PartiallyCreated,
    /// <summary>Dispatched without a provable answer; reconciliation only, never an automatic retry.</summary>
    OutcomeUnknown,
    /// <summary>A person linked the OR number after reconciliation.</summary>
    Linked,
    /// <summary>A person searched the ITSM and stated that no OR exists (reason mandatory).</summary>
    ConfirmedNotCreated
}

/// <summary>Inputs of the submission fingerprint: everything that is sent or decides what is sent.</summary>
/// <param name="PlanId">Plan.</param>
/// <param name="AccountId">Account.</param>
/// <param name="PreviewVersion">Approved preview version.</param>
/// <param name="PreviewSha256">Approved preview digest.</param>
/// <param name="TargetGmsaName">Target gMSA name of the account in the plan.</param>
/// <param name="Servers">Servers of the account in the approved preview.</param>
/// <param name="Provider">Creating provider.</param>
/// <param name="RequesterUserId">ITSM user id of the person who presses the button.</param>
/// <param name="RequesterHomeLocationId">That user's home location id.</param>
/// <param name="Environment">Chosen environment.</param>
/// <param name="ShortDescription">Exact short description.</param>
/// <param name="Description">Exact description including the plan reference line.</param>
public sealed record OperationalRecordFingerprintInput(Guid PlanId, Guid AccountId, int PreviewVersion, string PreviewSha256, string TargetGmsaName,
    IReadOnlyList<string> Servers, string Provider, string RequesterUserId, string? RequesterHomeLocationId, string Environment, string ShortDescription,
    string Description);

/// <summary>
/// Pure rules for opening an OR from an approved gMSA change plan (ADR-0030, PROPOSED; OR-2 with a Fake adapter). The server
/// builds the text from a template, the person may edit it in the preview, and what is sent is exactly what was previewed: the
/// fingerprint binds the confirmation to that text, the requester mapping, the environment and the approved preview.
/// </summary>
public static class OperationalRecordRules
{
    /// <summary>Contract version inside every fingerprint; a change invalidates every open preview.</summary>
    public const string ContractVersion = "sa-or-1";

    /// <summary>Description limit including the plan reference line (ADR-0030 §2).</summary>
    public const int MaxDescription = 4000;

    /// <summary>Upper bound of the configurable short description limit (column size, migration 035).</summary>
    public const int MaxShortDescriptionColumn = 400;

    /// <summary>Reconciliation reason limit.</summary>
    public const int MaxReason = 1000;

    /// <summary>A live submission blocks a new attempt for the same plan account (filtered unique index, migration 035).</summary>
    public static bool BlocksNewSubmission(OperationalRecordState state) =>
        state is not (OperationalRecordState.NotSent or OperationalRecordState.ConfirmedNotCreated);

    /// <summary>States a person reconciles (search the ITSM, then link a number or confirm none exists).</summary>
    public static bool NeedsReconciliation(OperationalRecordState state) =>
        state is OperationalRecordState.Sending or OperationalRecordState.OutcomeUnknown or OperationalRecordState.PartiallyCreated;

    /// <summary>Linking a number is allowed from every state that needs reconciliation.</summary>
    public static bool CanLink(OperationalRecordState state) => NeedsReconciliation(state);

    /// <summary>
    /// "Not created" is allowed only when the insert itself is unproven; a PartiallyCreated record exists (its internal id is
    /// known), so it can only be linked.
    /// </summary>
    public static bool CanConfirmNotCreated(OperationalRecordState state) =>
        state is OperationalRecordState.Sending or OperationalRecordState.OutcomeUnknown;

    /// <summary>
    /// A Sending row younger than the dispatch bound may still be in flight in another request; reconciling it could overwrite
    /// the real outcome, so it waits until the bound plus a margin has passed.
    /// </summary>
    public static bool MayStillBeInFlight(OperationalRecordState state, DateTimeOffset createdAt, DateTimeOffset now, TimeSpan dispatchBound) =>
        state == OperationalRecordState.Sending && now - createdAt < dispatchBound + TimeSpan.FromMinutes(1);

    /// <summary>Plan reference appended as the description's last line so a person finds the OR during reconciliation.</summary>
    public static string PlanReference(Guid planId, Guid accountId) => $"WASAS plan {planId:D} / hesap {accountId:D}";

    /// <summary>Default short description (owner's masked sample pattern).</summary>
    public static string DefaultShortDescription(string targetGmsaName) => $"gmsa service account talebi({targetGmsaName})";

    /// <summary>Default editable description body: gMSA name, servers of the approved preview, fixed request text.</summary>
    public static string DefaultDescription(string targetGmsaName, IReadOnlyList<string> servers)
    {
        StringBuilder builder = new();
        builder.Append("Talep: gMSA oluşturulması (servis hesabının gMSA'ya geçişi).\n");
        builder.Append("gMSA adı: ").Append(targetGmsaName).Append('\n');
        builder.Append("gMSA parolasını alabilecek sunucular:\n");
        if (servers.Count == 0)
        {
            builder.Append("- (onaylı önizlemede sunucu yok; ekleyin)\n");
        }

        foreach (string server in servers)
        {
            builder.Append("- ").Append(server).Append('\n');
        }

        builder.Append("Not: Parola bu kayıtta yer almaz; değişikliği ekip kendi değişiklik kaydıyla yapar.");
        return builder.ToString();
    }

    /// <summary>Line endings become LF and outer whitespace is trimmed; the result is what the preview shows and the fingerprint covers.</summary>
    public static string NormalizeText(string? value) =>
        (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

    /// <summary>The description that is sent: the edited body, a blank line, and the plan reference line.</summary>
    public static string ComposeDescription(string body, string planReference) => body.Length == 0 ? planReference : body + "\n\n" + planReference;

    /// <summary>
    /// First invalid field of normalized texts, or null: both required, within their limits (the description counted with the
    /// reference line), and no control character except the line break (no tab, no NUL, no bidirectional override).
    /// </summary>
    public static string? TextError(string shortDescription, string body, string planReference, int shortDescriptionMax)
    {
        if (shortDescription.Length == 0 || shortDescription.Length > shortDescriptionMax || shortDescription.Contains('\n') || HasForbiddenCharacter(shortDescription))
        {
            return "shortDescription";
        }

        return body.Length == 0 || ComposeDescription(body, planReference).Length > MaxDescription || HasForbiddenCharacter(body) ? "description" : null;
    }

    /// <summary>Servers of one account in the approved preview: distinct, ordered, no empty names.</summary>
    public static IReadOnlyList<string> Servers(IEnumerable<string?> serverNames) =>
        [.. serverNames.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// SHA-256 (lowercase hex) of the canonical submission. Every field is written as <c>name=length:value</c> on its own line in
    /// a fixed order, so no two different submissions share a byte sequence; servers are sorted. Computed on the server only.
    /// </summary>
    public static string Fingerprint(OperationalRecordFingerprintInput input)
    {
        StringBuilder canonical = new();
        void Field(string name, string? value) =>
            canonical.Append(name).Append('=').Append(value?.Length ?? -1).Append(':').Append(value).Append('\n');
        Field("contract", ContractVersion);
        Field("plan", input.PlanId.ToString("D"));
        Field("account", input.AccountId.ToString("D"));
        Field("previewVersion", input.PreviewVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Field("previewSha256", input.PreviewSha256);
        Field("gmsa", input.TargetGmsaName);
        foreach (string server in input.Servers.Order(StringComparer.OrdinalIgnoreCase))
        {
            Field("server", server);
        }

        Field("provider", input.Provider);
        Field("requester", input.RequesterUserId);
        Field("requesterLocation", input.RequesterHomeLocationId);
        Field("environment", input.Environment);
        Field("shortDescription", input.ShortDescription);
        Field("description", input.Description);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    /// <summary>Turkish label; "Belirsiz" is never shown as success or failure.</summary>
    public static string StateLabel(OperationalRecordState state) => state switch
    {
        OperationalRecordState.Sending => "Gönderiliyor — sonuç kaydedilmedi",
        OperationalRecordState.NotSent => "Gönderilmedi",
        OperationalRecordState.Created => "OR açıldı",
        OperationalRecordState.PartiallyCreated => "Kısmen oluşturuldu — eksikleri Turuncu Hat'ta tamamlayın",
        OperationalRecordState.OutcomeUnknown => "Belirsiz — OR açılmış olabilir",
        OperationalRecordState.Linked => "Elle bağlandı",
        _ => "Açılmadığı teyit edildi"
    };

    /// <summary>Turkish label of an outcome reason code (migration 035 <c>OutcomeReason</c>); unknown codes are shown as they are.</summary>
    public static string ReasonLabel(string code) => code switch
    {
        "NotConfigured" => "OR açma bu ortamda kapalı",
        "RequesterLocationMissing" => "Turuncu Hat kullanıcınızın konumu yok; hiçbir şey gönderilmedi",
        "AuthenticationFailed" => "Turuncu Hat oturumu açılamadı; hiçbir şey gönderilmedi",
        "Timeout" => "Turuncu Hat zamanında cevap vermedi",
        "Unavailable" => "Turuncu Hat'a ulaşılamadı",
        "Rejected" => "Turuncu Hat isteği reddetti; bir şey yazılıp yazılmadığı kanıtlanmadı",
        "InvalidResponse" => "Turuncu Hat'ın cevabı okunamadı veya çelişkili",
        "NumberNotReadBack" => "Kayıt oluştu ama OR numarası okunamadı",
        "PropertyFailed" => "Kayıt oluştu ama bir form alanı yazılamadı",
        "DispatchInterrupted" => "Gönderim yarıda kaldı; sonuç bilinmiyor",
        _ => code
    };

    private static bool HasForbiddenCharacter(string value) =>
        value.Any(c => c != '\n' && (char.IsControl(c) || c is '‪' or '‫' or '‬' or '‭' or '‮' or '⁦' or '⁧' or '⁨' or '⁩'));
}
