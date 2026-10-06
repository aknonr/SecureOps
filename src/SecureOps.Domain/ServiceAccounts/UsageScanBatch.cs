namespace SecureOps.Domain.ServiceAccounts;

/// <summary>What happened to one account when one usage-scan file was attached to several accounts at once (ADR-0027).</summary>
public enum UsageScanBatchOutcome
{
    /// <summary>A new link from the scan to the account was written.</summary>
    Attached,
    /// <summary>This person had already attached the same file to the account; nothing changed.</summary>
    AlreadyAttached,
    /// <summary>The file did not search this account's name (or searched it in another domain).</summary>
    NotInScan,
    /// <summary>The file searched the name in two domains and the account has no domain: the person decides (fail closed).</summary>
    Ambiguous,
    /// <summary>Missing, out of scope or not the caller's to work on (indistinguishable on purpose).</summary>
    Unavailable,
    /// <summary>Storing the link failed for this account; repeating the same upload is safe (already linked accounts answer AlreadyAttached).</summary>
    Failed
}

/// <summary>Bounds and labels for attaching one usage-scan file to several accounts in one upload.</summary>
public static class UsageScanBatch
{
    /// <summary>Most accounts per upload (same bound as a multi-account mail record).</summary>
    public const int MaxAccounts = 20;

    /// <summary>
    /// The account outcome from what is known about it: visible to the caller, responsible for it (may work on the whole
    /// account), and the searched name of the file under which it would attach. Visibility and responsibility come first,
    /// so a file never tells the caller anything about an account it may not work on.
    /// </summary>
    public static UsageScanBatchOutcome Precheck(bool visible, bool responsible, string? matchedName, bool ambiguous) =>
        !visible || !responsible ? UsageScanBatchOutcome.Unavailable
        : matchedName is not null ? UsageScanBatchOutcome.Attached
        : ambiguous ? UsageScanBatchOutcome.Ambiguous
        : UsageScanBatchOutcome.NotInScan;

    /// <summary>Turkish label of an outcome; "not in the file" never means "not used".</summary>
    public static string Label(UsageScanBatchOutcome value) => value switch
    {
        UsageScanBatchOutcome.Attached => "Bağlandı",
        UsageScanBatchOutcome.AlreadyAttached => "Bu dosyayı bu hesaba daha önce bağlamıştınız; değişiklik yok",
        UsageScanBatchOutcome.NotInScan => "Dosya bu hesabı aramamış; bağlanmadı",
        UsageScanBatchOutcome.Ambiguous => "Belirsiz: dosya bu adı iki farklı domainde aramış ve hesabın domaini kayıtlı değil; bağlanmadı",
        UsageScanBatchOutcome.Unavailable => "Bulunamadı veya bu hesapta tarama bağlama yetkiniz yok; bağlanmadı",
        _ => "Kaydedilemedi; aynı yüklemeyi tekrarlamak güvenlidir"
    };
}
