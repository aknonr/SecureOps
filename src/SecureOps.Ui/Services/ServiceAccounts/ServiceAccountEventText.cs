using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>Where the "what happened?" flow opens: an event kind and a request, both optional (the person can change them).</summary>
/// <param name="EventKind">Pre-selected event kind; null starts at the choice.</param>
/// <param name="RequestId">Pre-selected open request.</param>
public sealed record SaEventStart(string? EventKind, Guid? RequestId);

/// <summary>
/// Operator wording of the "what happened?" flow (EVENT-GUIDANCE-DESIGN.md §3–§7, §9). Labels only: which records an event writes, the
/// close rule and every permission are decided by the server; the event table (<see cref="ServiceAccountEventRules"/>) is read here
/// only to show which fields an event takes, and the server rejects anything else.
/// </summary>
public static class ServiceAccountEventText
{
    /// <summary>The label every reported action carries, everywhere in the flow.</summary>
    public const string Reported = "Bildirildi — doğrulanmadı";

    /// <summary>One event card: kind, title, one-sentence explanation and what the product records.</summary>
    public sealed record EventCard(string Kind, string Title, string Explanation, string Records);

    /// <summary>The seven events of §3, in the order the person sees them.</summary>
    public static IReadOnlyList<EventCard> Cards { get; } =
    [
        new(nameof(AccountEventKind.OwnerReplied), "Sahip yanıt verdi", "Hesabın sahibi veya muhatap ekip yazdığınız maile cevap verdi.",
            "Gelen yanıt yazışması; bağlı talebin son yanıt tarihi ileri alınır."),
        new(nameof(AccountEventKind.PasswordChanged), "Parola değişti (kişi bildirdi)", "Biri parolanın değiştiğini bildirdi. Parolayı PAM değiştirir; ürün parola görmez, değiştirmez.",
            "Parola değişimi işlem bildirimi (" + Reported + "); isteğe bağlı yazışma ve OCO/OR numarası."),
        new(nameof(AccountEventKind.AccountDeleted), "Hesap silindi (kişi bildirdi)", "Biri hesabın silindiğini bildirdi. Hesap bu kayıtla kapanmaz.",
            "Silme işlem bildirimi, kapanış adayı (" + Reported + "); OR numarası olmadan silme sonradan doğrulanamaz."),
        new(nameof(AccountEventKind.GmsaConverted), "gMSA'ya geçildi (kişi bildirdi)", "Biri hesabın gMSA'ya çevrildiğini bildirdi. Plan kontrol listesi kendiliğinden işaretlenmez.",
            "gMSA geçişi işlem bildirimi, kapanış adayı (" + Reported + "); isteğe bağlı OCO/OR numarası."),
        new(nameof(AccountEventKind.OwnerChanged), "Sahip değişti", "Hesabın sorumlu ekibi veya kişisi değişti. Karar sahiplik akışında verilir.",
            "Yalnız sahiplik önerisi (karar değil); isteğe bağlı yazışma."),
        new(nameof(AccountEventKind.ServiceMoved), "Servis taşındı", "Hesabı kullanan bir servis başka sunucuya taşındı. Kullanım kaydı değişmez.",
            "Yazışma kaydı; isteğe bağlı inceleme talebi (kullanımın yeniden taranması)."),
        new(nameof(AccountEventKind.Other), "Başka bir gelişme", "Yukarıdakilerden hiçbiri değil.",
            "Yazışma kaydı; isteğe bağlı değerlendirme talebi.")
    ];

    /// <summary>The card of a kind (null for an unknown kind).</summary>
    public static EventCard? Card(string? kind) => Cards.FirstOrDefault(c => c.Kind == kind);

    /// <summary>The event table row of a kind, or null for an unknown kind.</summary>
    public static AccountEventShape? Shape(string? kind) =>
        Enum.TryParse(kind, false, out AccountEventKind parsed) && parsed.ToString() == kind ? ServiceAccountEventRules.Shape(parsed) : null;

    /// <summary>
    /// The event that reports a request's expected work (for "Bildir" on a request or a next step): password change, deletion and
    /// gMSA work have their own event; any other request is answered by the owner's reply.
    /// </summary>
    public static string EventForRequest(string actionType) => actionType switch
    {
        nameof(ServiceAccountActionType.PasswordChange) => nameof(AccountEventKind.PasswordChanged),
        nameof(ServiceAccountActionType.Deletion) => nameof(AccountEventKind.AccountDeleted),
        nameof(ServiceAccountActionType.GmsaConversion) or nameof(ServiceAccountActionType.GmsaHandover) => nameof(AccountEventKind.GmsaConverted),
        _ => nameof(AccountEventKind.OwnerReplied)
    };

    /// <summary>
    /// Open requests an event may link, for the caller: the server's request-type match (an action-producing event links only a
    /// request that accepts its action) and, for a participant, only its own requests. The server checks both again.
    /// </summary>
    public static IReadOnlyList<RequestView> LinkableRequests(AccountDetail detail, string? kind)
    {
        if (Shape(kind) is not { LinkRequest: true } shape)
        {
            return [];
        }

        return [.. detail.Requests.Where(r => r.Status == "Open" && detail.Permissions.CanWorkRequest(r.Id)
            && (shape.ActionType is not { } action || Enum.TryParse(r.ActionType, false, out ServiceAccountActionType type) && ServiceAccountRules.RequestAccepts(type, action)))];
    }

    /// <summary>Whether the caller may start the flow at all, or why not (null when allowed).</summary>
    public static string? WhyNoFlow(AccountPermissions permissions) =>
        permissions.Work ? null
        : permissions.CanWorkAnyRequest ? null
        : "Bu hesapta kayıt yetkiniz yok: hesaptan sorumlu ekipte iş yetkisi (Work) veya ekibinize atanmış açık bir talep gerekir. Gelişmeyi sorumlu ekibe iletin.";

    /// <summary>Kinds a caller may choose: a participant only records on its own request, so only the request-linked events.</summary>
    public static IReadOnlyList<EventCard> CardsFor(AccountPermissions permissions) =>
        permissions.Work ? Cards : [.. Cards.Where(c => Shape(c.Kind) is { LinkRequest: true })];

    /// <summary>Channel choices.</summary>
    public static IReadOnlyList<(string Value, string Label)> Channels { get; } =
        [("Mail", "Mail"), ("Phone", "Telefon"), ("Meeting", "Toplantı"), ("Other", "Diğer")];

    /// <summary>Reference types the external record rule accepts.</summary>
    public static IReadOnlyList<string> ReferenceTypes { get; } = ["OCO", "OR", "JIRA", "OTHER"];

    /// <summary>Preview item kind in the operator's words.</summary>
    public static string ItemKind(string kind) => kind switch
    {
        "Communication" => "Yazışma",
        "Action" => "İşlem bildirimi",
        "Reference" => "Referans",
        "RequestUpdate" => "Talep güncellemesi",
        "RequestCreate" => "Yeni talep",
        "OwnershipProposal" => "Sahiplik önerisi",
        "RequestClose" => "Talep kapatma",
        _ => kind
    };

    /// <summary>Preview operation.</summary>
    public static string Operation(string operation) => operation switch
    {
        "Create" => "oluşturulur",
        "Update" => "güncellenir",
        "Link" => "bağlanır",
        _ => operation
    };

    /// <summary>Field name of a preview item value.</summary>
    public static string FieldName(string name) => name switch
    {
        "direction" => "Yön",
        "kind" => "Tür",
        "occurredOn" => "Tarih",
        "contactTeamId" => "Muhatap ekip",
        "contactPersonId" => "Muhatap kişi",
        "subject" => "Konu",
        "summary" => "Kısa özet",
        "meaningfulReply" => "Anlamlı yanıt",
        "providerMessageId" => "Mail kimliği",
        "actionType" => "İşlem türü",
        "result" => "Sonuç",
        "recordKind" => "Kayıt türü",
        "actualOn" => "Gerçekleşme tarihi",
        "requestId" => "Talep",
        "performerTeamId" => "Yapan ekip",
        "performerPersonId" => "Yapan kişi",
        "evidenceNote" => "Kanıt notu",
        "type" => "Tür",
        "number" => "Numara",
        "url" => "Bağlantı",
        "lastReplyOn" => "Yeni son yanıt tarihi",
        "previousLastReplyOn" => "Önceki son yanıt tarihi",
        "targetTeamId" => "Hedef ekip",
        "notes" => "Not",
        "mode" => "Biçim",
        "teamId" => "Ekip",
        "personId" => "Kişi",
        "effectiveFrom" => "Geçerlilik",
        "reason" => "Gerekçe",
        "outcome" => "Kapanış sonucu",
        _ => name
    };

    /// <summary>
    /// A preview value in the operator's words: codes become labels, directory ids become names when the caller's lists know them,
    /// empty values say so. The server's value is never replaced by a guess.
    /// </summary>
    public static string FieldValue(EventField field, AccountDetail detail, IReadOnlyList<TeamView> teams)
    {
        if (field.Value is not { Length: > 0 } value)
        {
            return field.Name is "occurredOn" or "actualOn" ? "Tarih bilinmiyor" : "—";
        }

        return field.Name switch
        {
            "direction" => ServiceAccountUiText.Direction(value),
            "kind" => Enum.TryParse(value, false, out CommunicationKind _) ? ServiceAccountUiText.CommunicationKind(value) : value,
            "actionType" => ServiceAccountUiText.Action(value),
            "result" => value == nameof(ServiceAccountActionResult.Performed) ? Reported : value,
            "recordKind" => value == nameof(ServiceAccountRecordKind.Closure) ? "Hesap kapanışı adayı (kapanış değil)" : "Ara adım",
            "occurredOn" or "actualOn" or "lastReplyOn" or "previousLastReplyOn" or "effectiveFrom" =>
                DateOnly.TryParseExact(value, "yyyy-MM-dd", out DateOnly date) ? ServiceAccountUiText.Date(date) : value,
            "contactTeamId" or "performerTeamId" or "targetTeamId" or "teamId" =>
                Guid.TryParse(value, out Guid team) ? teams.FirstOrDefault(t => t.Id == team)?.Name ?? "Seçilen ekip" : value,
            "contactPersonId" or "performerPersonId" or "personId" => "Seçilen kişi",
            "requestId" => Guid.TryParse(value, out Guid request) ? detail.Requests.FirstOrDefault(r => r.Id == request)?.ActionLabel ?? "Seçilen talep" : value,
            "meaningfulReply" => value == "True" ? "Evet" : "Hayır",
            "providerMessageId" => "Var",
            "mode" => "Öneri (karar değil)",
            "outcome" => ServiceAccountUiText.Outcome(value),
            _ => value
        };
    }

    /// <summary>Why a preview item cannot be written by the caller.</summary>
    public static string Denied(string? reason) => reason switch
    {
        "NoWorkCapability" => "Yapılamaz: iş yetkiniz (Work) yok.",
        "OutOfScope" => "Yapılamaz: hesap kapsamınızda değil.",
        "NotParticipantRequest" => "Yapılamaz: yalnız ekibinize atanmış talep üzerinden kayıt yapabilirsiniz.",
        _ => "Yapılamaz: yetki."
    };

    /// <summary>What the event will not do (always shown).</summary>
    public static string NotRecorded(string code) => code switch
    {
        "Verification" => "Doğrulama yapılmaz. Bildirilen işlem doğrulanmamış kalır; doğrulama ayrı adımdır (doğrulama yetkisi ve kanıt).",
        "AccountClosure" => "Hesap kapatılmaz.",
        "RequestClose" => "Talep kapatılmaz (seçilmedi).",
        "OwnershipDecision" => "Sahiplik kararı verilmez; öneri sahiplik akışında karara bağlanır.",
        "PlanChecklist" => "Değişiklik planının kontrol listesi işaretlenmez.",
        _ => code
    };

    /// <summary>A preview warning.</summary>
    public static string Warning(string code) => code switch
    {
        "OrMissingForDeletion" => "OR numarası yok: kayıt yapılır ama silme kapanışı sonradan OR olmadan doğrulanamaz.",
        "DateUnknown" => "Tarih bilinmiyor: tarihsiz bildirim olarak kaydedilir, tarih uydurulmaz.",
        "MailDuplicate" => "Aynı mail kimliğiyle yazışma zaten kayıtlı; yenisi açılmaz, bu hesaba bağlanır.",
        "ReplyDateEarlier" => "Talebin son yanıt tarihi zaten bu tarihte veya daha ileride; geri alınmaz.",
        "ReplyDateUnknown" => "Tarih bilinmediği için talebin son yanıt tarihi değişmez.",
        "DateAssumed" => "Tarihin yılı varsayıldı; doğru olduğunu kontrol edin.",
        _ => code
    };

    /// <summary>A step still to take after saving (never shown as done).</summary>
    public static string FollowUp(string kind) => kind switch
    {
        "Verify" => "Bildirilen işlemi doğrulama yetkisi olan kişi kanıtla doğrulamalı (İşler sekmesi → İşlem bildirimleri).",
        "CloseRequest" => "Talep açık kaldı; iş bittiğinde İşler sekmesinden kapatın.",
        "OpenPlan" => "Açık bir değişiklik planı varsa kontrol listesini plan sayfasında işaretleyin.",
        "UsageTab" => "Taşınan servisin kullanımını Kullanım ve kural / Kullanım taraması sekmesinde güncelleyin. Bulunamadı, kullanılmıyor demek değildir.",
        "OwnershipDecision" => "Sahiplik önerisini teyit yetkisi olan kişi Sahiplik sekmesinde karara bağlamalı.",
        _ => kind
    };

    /// <summary>The detail tab a follow-up opens, or null when it points elsewhere (the plan list).</summary>
    public static SaTab? FollowUpTab(string kind) => kind switch
    {
        "Verify" or "CloseRequest" => SaTab.Work,
        "UsageTab" => SaTab.Usage,
        "OwnershipDecision" => SaTab.Ownership,
        _ => null
    };

    /// <summary>A created record of the commit result.</summary>
    public static string Created(EventCreatedRecord record) => $"{ItemKind(record.Kind)} {Operation(record.Operation)}"
        + (record.Kind == "Action" ? " · " + Reported : string.Empty);

    /// <summary>A missing close condition in the operator's words (the server's code; the rule is not repeated here).</summary>
    public static string Missing(CloseMissing missing)
    {
        string text = ServiceAccountProblems.FieldText(missing.Code) ?? missing.Code;
        if (missing.Code == "NoMatchingPerformedAction" && missing.ExpectedActionType is { } expected)
        {
            text = $"Talebi tamamlandı olarak kapatmak için gerçekleşmiş bir \"{ServiceAccountUiText.Action(expected)}\" bildirimi gerekir.";
        }

        return missing.SubCodes is { Count: > 0 } sub ? text + " Eksik: " + string.Join(" ", sub.Select(SubCode)) : text;
    }

    /// <summary>One unmet condition of the closest candidate action.</summary>
    public static string SubCode(string code) => code switch
    {
        "NotVerified" => "İşlem doğrulanmadı.",
        "NotClosureKind" => "İşlem hesap kapanışı olarak bildirilmedi.",
        _ => ServiceAccountProblems.FieldText(code) ?? code
    };

    /// <summary>What clears a missing condition.</summary>
    public static string Remedy(string remedy) => remedy switch
    {
        "ReportAction" => "Gerçekleşen işlemi bildirin.",
        "VerifyAction" => "Doğrulama yetkisi olan kişi işlemi kanıtla doğrulamalı.",
        "ConfirmOwnership" => "Sahipliği Sahiplik sekmesinde teyit ettirin.",
        "GiveReason" => "Gerekçe yazın.",
        _ => string.Empty
    };

    /// <summary>The visible "why not" lines of a readiness, without the reason line once a reason is typed (it is written at close time).</summary>
    public static IReadOnlyList<CloseMissing> Blocking(CloseReadiness readiness, bool reasonTyped) =>
        [.. readiness.Missing.Where(m => !(reasonTyped && m.Code == "CloseReasonRequired"))];

    /// <summary>A mail-hint rule in the operator's words.</summary>
    public static string HintWarning(string code) => code switch
    {
        "PasswordLikeContent" => "Metinde parola olabilecek bir ifade var. Kaydedilmedi; metni silip yalnız ilgili cümleyi yapıştırın.",
        "NothingFound" => "Ürün metinden otomatik bir öneri çıkaramadı. Bu, mailde bilgi olmadığı anlamına gelmez; alanları elle doldurun.",
        _ => code
    };
}
