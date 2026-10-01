namespace SecureOps.Domain.ServiceAccounts;

/// <summary>Where a service account is used; controlled vocabulary of the knowledge-base rules.</summary>
public enum UsageKind
{
    /// <summary>Database access.</summary>
    Database,
    /// <summary>File server / share access.</summary>
    FileShare,
    /// <summary>Runs a scheduled task.</summary>
    ScheduledTask,
    /// <summary>Runs a Windows service.</summary>
    WindowsService,
    /// <summary>Application itself runs from a file server / UNC path.</summary>
    UncApplication,
    /// <summary>IIS virtual directory / virtual path pointing to a file share.</summary>
    IisVirtualDirectory,
    /// <summary>IIS application pool identity (no knowledge-base rule).</summary>
    IisAppPool,
    /// <summary>Other usage (no knowledge-base rule).</summary>
    Other
}

/// <summary>Database engine of a database usage.</summary>
public enum DatabaseEngine
{
    /// <summary>Not known yet.</summary>
    Unknown,
    /// <summary>Microsoft SQL Server.</summary>
    SqlServer,
    /// <summary>Oracle.</summary>
    Oracle,
    /// <summary>Another engine.</summary>
    Other
}

/// <summary>Path a rule recommends. A recommendation never changes data and never means gMSA suitability (rule 10).</summary>
public enum RecommendedPath
{
    /// <summary>Usage information is missing; no path can be chosen.</summary>
    NeedsInformation,
    /// <summary>Verify the real need and dependency first (Windows service).</summary>
    VerifyNeed,
    /// <summary>The account serves work that belongs to different teams; splitting must be evaluated.</summary>
    SplitAccount,
    /// <summary>Evaluate as gMSA; the configured gMSA executing team performs it.</summary>
    GmsaEvaluation,
    /// <summary>The solution team takes the account (normal/domain service account).</summary>
    SolutionTeamHandover,
    /// <summary>
    /// No service account is needed; evaluate removal. Reserved for an explicitly approved business rule: no current rule
    /// produces it (the Oracle note of the knowledge base is not an approved rule).
    /// </summary>
    NoServiceAccountNeeded,
    /// <summary>Move the application files to the server's local disk.</summary>
    MoveToLocalDisk,
    /// <summary>Remove the IIS virtual directory dependency; reach the resource from code/configuration.</summary>
    RemoveVirtualDirectoryDependency,
    /// <summary>Scheduled task usage is not preferred; research an alternative.</summary>
    SeekAlternative,
    /// <summary>No knowledge-base rule covers the usage; a person decides.</summary>
    ManualDecision
}

/// <summary>Whether the account's work follows its recommended path.</summary>
public enum RuleConformance
{
    /// <summary>No active usage recorded and no team rule applies.</summary>
    NotAssessed,
    /// <summary>Usage recorded but information is missing.</summary>
    IncompleteInformation,
    /// <summary>Non-preferred state without matching planned or completed work: against the rule.</summary>
    Unplanned,
    /// <summary>Matching work is open or in progress.</summary>
    Planned,
    /// <summary>The expected work is completed and verified.</summary>
    Completed,
    /// <summary>Every rule item is covered by a reasoned, authorized exception.</summary>
    Exception,
    /// <summary>No approved rule decides the path; a person must review. Not counted as against the rule.</summary>
    ManualReviewPending
}

/// <summary>One active usage of an account.</summary>
/// <param name="Id">Usage.</param>
/// <param name="AccountId">Account.</param>
/// <param name="Kind">Usage kind.</param>
/// <param name="Engine">Database engine (database usage only).</param>
/// <param name="NeedVerified">Windows service: real need and dependency verified.</param>
/// <param name="HasException">A reasoned exception was recorded by an authorized verifier.</param>
public sealed record UsageFact(Guid Id, Guid AccountId, UsageKind Kind, DatabaseEngine? Engine, bool? NeedVerified, bool HasException);

/// <summary>Work state of one account that decides conformance; derived from existing requests, handovers and actions.</summary>
/// <param name="OpenRequestTypes">Action types of the account's open requests.</param>
/// <param name="HasGmsaTransition">A gMSA transition record exists.</param>
/// <param name="GmsaConverted">A non-void performed gMSA conversion exists.</param>
/// <param name="GmsaVerifiedClosure">A verified gMSA conversion closure exists.</param>
/// <param name="DeletionVerifiedClosure">A verified deletion closure exists.</param>
/// <param name="HandoverProposed">A handover is proposed.</param>
/// <param name="HandoverAccepted">A handover is accepted with acceptance evidence.</param>
public sealed record AccountWorkState(
    IReadOnlySet<ServiceAccountActionType> OpenRequestTypes,
    bool HasGmsaTransition,
    bool GmsaConverted,
    bool GmsaVerifiedClosure,
    bool DeletionVerifiedClosure,
    bool HandoverProposed,
    bool HandoverAccepted)
{
    /// <summary>No work at all.</summary>
    public static AccountWorkState None { get; } = new(new HashSet<ServiceAccountActionType>(), false, false, false, false, false, false);
}

/// <summary>One explained rule result.</summary>
/// <param name="RuleCode">Stable rule code shown to the user.</param>
/// <param name="Path">Recommended path.</param>
/// <param name="Reason">Why this recommendation ("neden bu öneri").</param>
/// <param name="UsageId">Usage that triggered it; null for a team rule.</param>
/// <param name="Excepted">Covered by a reasoned exception.</param>
public sealed record RuleItem(string RuleCode, RecommendedPath Path, string Reason, Guid? UsageId, bool Excepted);

/// <summary>Rule evaluation of one account.</summary>
/// <param name="AccountId">Account.</param>
/// <param name="Path">Primary path; null when nothing is assessed.</param>
/// <param name="Conformance">Conformance of the primary path.</param>
/// <param name="Items">All rule items, excepted ones included.</param>
public sealed record AccountRuleEvaluation(Guid AccountId, RecommendedPath? Path, RuleConformance Conformance, IReadOnlyList<RuleItem> Items);

/// <summary>
/// Knowledge-base rules for service account requests, plus the 2026-10-01 owner decision that accounts of SQL teams are
/// evaluated as gMSA by the configured executing team. Pure and deterministic; the result explains itself and never
/// changes data, ownership, suitability or closure.
/// </summary>
public static class ServiceAccountUsageRules
{
    /// <summary>Rule set version stamped on reports.</summary>
    public const string RuleSetVersion = "kb-2026-08-11+karar-2026-10-01+r2";

    /// <summary>Primary path precedence: preconditions first, then a single handover target, then dependency removals.</summary>
    private static readonly RecommendedPath[] _precedence =
    [
        RecommendedPath.NeedsInformation,
        RecommendedPath.VerifyNeed,
        RecommendedPath.SplitAccount,
        RecommendedPath.GmsaEvaluation,
        RecommendedPath.SolutionTeamHandover,
        RecommendedPath.NoServiceAccountNeeded,
        RecommendedPath.MoveToLocalDisk,
        RecommendedPath.RemoveVirtualDirectoryDependency,
        RecommendedPath.SeekAlternative,
        RecommendedPath.ManualDecision
    ];

    /// <summary>Paths that hand the account to a different owner (or retire it); two different ones mean a split.</summary>
    private static readonly HashSet<RecommendedPath> _targets =
        [RecommendedPath.GmsaEvaluation, RecommendedPath.SolutionTeamHandover, RecommendedPath.NoServiceAccountNeeded];

    /// <summary>Evaluates one account's active usages and team rule against its current work.</summary>
    /// <param name="accountId">Account.</param>
    /// <param name="usages">Active usages of the account.</param>
    /// <param name="sqlTeamAccount">The account belongs to a team configured as an SQL team.</param>
    /// <param name="executorTeam">Configured gMSA executing team label, if any.</param>
    /// <param name="work">Current work state.</param>
    public static AccountRuleEvaluation Evaluate(Guid accountId, IEnumerable<UsageFact> usages, bool sqlTeamAccount, string? executorTeam, AccountWorkState work)
    {
        UsageFact[] active = [.. usages];
        List<RuleItem> items = [.. active.Select(u => Item(u, executorTeam)).OfType<RuleItem>()];
        if (sqlTeamAccount)
        {
            items.Insert(0, new RuleItem("SQL-EKIP", RecommendedPath.GmsaEvaluation,
                $"Hesap SQL ekibi olarak tanımlı bir ekibe ait. 1 Ekim 2026 kararıyla SQL ekiplerindeki hesaplar gMSA olarak değerlendirilir ve {Executor(executorTeam)} atanır.",
                null, false));
        }

        // A verified Windows service whose accessed resource is not recorded still needs information.
        if (active.Length > 0 && items.Count == 0)
        {
            items.Add(new RuleItem("KB-SERVIS-KAYNAK", RecommendedPath.NeedsInformation,
                "Windows servisinin ihtiyacı doğrulandı; servisin eriştiği kaynak (veritabanı, dosya sunucusu) ayrıca kullanım olarak girilmeli.", null, false));
        }

        RuleItem[] open = [.. items.Where(i => !i.Excepted)];
        if (items.Count == 0)
        {
            return new AccountRuleEvaluation(accountId, null, RuleConformance.NotAssessed, items);
        }

        if (open.Length == 0)
        {
            return new AccountRuleEvaluation(accountId, items.Select(i => i.Path).OrderBy(Rank).First(), RuleConformance.Exception, items);
        }

        RecommendedPath[] targets = [.. open.Select(i => i.Path).Where(_targets.Contains).Distinct()];
        if (targets.Length > 1)
        {
            items.Add(new RuleItem("KB-BOLUNME", RecommendedPath.SplitAccount,
                "Hesap farklı ekiplere ait işlere hizmet ediyor (" + string.Join(", ", targets.OrderBy(Rank).Select(PathLabel)) +
                "). Tek hesabın ayrılması değerlendirilmeli.", null, false));
            open = [.. items.Where(i => !i.Excepted)];
        }

        RecommendedPath path = open.Select(i => i.Path).OrderBy(Rank).First();
        return new AccountRuleEvaluation(accountId, path, Conformance(path, work), items);
    }

    /// <summary>Conformance of a primary path given the account's existing work.</summary>
    public static RuleConformance Conformance(RecommendedPath path, AccountWorkState work)
    {
        bool review = work.OpenRequestTypes.Contains(ServiceAccountActionType.Review) || work.OpenRequestTypes.Contains(ServiceAccountActionType.Evaluate);
        return path switch
        {
            RecommendedPath.NeedsInformation => RuleConformance.IncompleteInformation,
            RecommendedPath.GmsaEvaluation => work.GmsaVerifiedClosure ? RuleConformance.Completed
                : work.HasGmsaTransition || work.GmsaConverted || work.OpenRequestTypes.Contains(ServiceAccountActionType.GmsaHandover)
                  || work.OpenRequestTypes.Contains(ServiceAccountActionType.GmsaConversion) ? RuleConformance.Planned
                : RuleConformance.Unplanned,
            RecommendedPath.NoServiceAccountNeeded => work.DeletionVerifiedClosure ? RuleConformance.Completed
                : work.OpenRequestTypes.Contains(ServiceAccountActionType.Deletion) || review ? RuleConformance.Planned : RuleConformance.Unplanned,
            RecommendedPath.SolutionTeamHandover => work.HandoverAccepted ? RuleConformance.Completed
                : work.HandoverProposed || review ? RuleConformance.Planned : RuleConformance.Unplanned,
            // Without an approved rule nothing can be "against the rule": the account waits for a person's review.
            RecommendedPath.ManualDecision => review ? RuleConformance.Planned : RuleConformance.ManualReviewPending,
            _ => review ? RuleConformance.Planned : RuleConformance.Unplanned
        };
    }

    private static RuleItem? Item(UsageFact usage, string? executorTeam) => usage.Kind switch
    {
        UsageKind.Database => usage.Engine switch
        {
            DatabaseEngine.SqlServer => new RuleItem("KB-VT-SQL", RecommendedPath.GmsaEvaluation,
                $"SQL Server veritabanı erişimi: mümkünse gMSA değerlendirilir. 1 Ekim 2026 kararıyla değerlendirme {Executor(executorTeam)} atanır. Uygunluk ayrıca karar ister; öneri uygunluk değildir.",
                usage.Id, usage.HasException),
            DatabaseEngine.Oracle => new RuleItem("KB-VT-ORACLE", RecommendedPath.ManualDecision,
                "Oracle veritabanı erişimi: onaylı bir iş kuralı yok (doğrulanmamış öneri). Bilgi bankasındaki \"Oracle ise servis hesabına gerek yok\" notu onaylı kural olarak uygulanmaz; hesap manuel incelenir. Kaldırma veya silme önerilmez.",
                usage.Id, usage.HasException),
            _ => new RuleItem("KB-VT-MOTOR", RecommendedPath.NeedsInformation,
                "Veritabanı motoru (SQL Server / Oracle) bilinmeden yol seçilemez.", usage.Id, usage.HasException)
        },
        UsageKind.FileShare => new RuleItem("KB-DOSYA", RecommendedPath.SolutionTeamHandover,
            "Dosya sunucusu erişimi uygulamanın çalışma şekline göre normal/domain servis hesabı gerektirebilir; hesabı çözüm ekibi almalı. Bizim ekibe ait hesap varsa çözüm ekibi/SRE ile teyitten sonra silinir ve ekip bilgilendirilir.",
            usage.Id, usage.HasException),
        UsageKind.ScheduledTask => new RuleItem("KB-GOREV", RecommendedPath.SeekAlternative,
            "Zamanlanmış görevde servis hesabı kullanımı tercih edilmez; alternatif yöntem araştırılır.", usage.Id, usage.HasException),
        UsageKind.WindowsService => usage.NeedVerified == true ? null : new RuleItem("KB-SERVIS", RecommendedPath.VerifyNeed,
            "Windows servisi üzerinde kullanılacaksa gerçekten ihtiyacı ve bağımlılığı doğrulanmalı.", usage.Id, usage.HasException),
        UsageKind.UncApplication => new RuleItem("KB-UNC", RecommendedPath.MoveToLocalDisk,
            "Uygulama dosya sunucusu/UNC yolu üzerinde çalışıyorsa mümkünse sunucunun yerel diskine taşınır (otomatik dağıtım için de gerekli).",
            usage.Id, usage.HasException),
        UsageKind.IisVirtualDirectory => new RuleItem("KB-IIS-SANAL", RecommendedPath.RemoveVirtualDirectoryDependency,
            "IIS sanal dizin/yol ile dosya paylaşımına erişiliyorsa bu bağımlılık kaldırılır; uygulama uzak kaynağa kod/yapılandırma (ör. appsettings.json) üzerinden erişir.",
            usage.Id, usage.HasException),
        _ => new RuleItem("KB-YOK", RecommendedPath.ManualDecision,
            "Bilgi bankasında bu kullanım için kural yok; karar ekip tarafından verilir.", usage.Id, usage.HasException)
    };

    private static string Executor(string? team) => team is null ? "gMSA yürütücü ekibe (ayar bekleniyor)" : $"{team} ekibine";

    private static int Rank(RecommendedPath path) => Array.IndexOf(_precedence, path);

    /// <summary>Turkish label of a recommended path.</summary>
    public static string PathLabel(RecommendedPath path) => path switch
    {
        RecommendedPath.NeedsInformation => "Bilgi eksik",
        RecommendedPath.VerifyNeed => "İhtiyacı doğrula",
        RecommendedPath.SplitAccount => "Hesap bölünmeli",
        RecommendedPath.GmsaEvaluation => "gMSA değerlendirmesi",
        RecommendedPath.SolutionTeamHandover => "Çözüm ekibine devir",
        RecommendedPath.NoServiceAccountNeeded => "Servis hesabı gerekmez",
        RecommendedPath.MoveToLocalDisk => "Yerel diske taşı",
        RecommendedPath.RemoveVirtualDirectoryDependency => "Sanal dizin bağımlılığını kaldır",
        RecommendedPath.SeekAlternative => "Alternatif araştır",
        _ => "Manuel karar"
    };

    /// <summary>Turkish label of a conformance state.</summary>
    public static string ConformanceLabel(RuleConformance value) => value switch
    {
        RuleConformance.NotAssessed => "Değerlendirilmedi",
        RuleConformance.IncompleteInformation => "Bilgi eksik",
        RuleConformance.Unplanned => "Kurala aykırı (plansız)",
        RuleConformance.Planned => "Planlı",
        RuleConformance.Completed => "Tamamlandı",
        RuleConformance.ManualReviewPending => "Manuel inceleme bekliyor",
        _ => "Gerekçeli istisna"
    };

    /// <summary>Turkish label of a usage kind.</summary>
    public static string UsageLabel(UsageKind kind) => kind switch
    {
        UsageKind.Database => "Veritabanı",
        UsageKind.FileShare => "Dosya sunucusu / paylaşım",
        UsageKind.ScheduledTask => "Zamanlanmış görev",
        UsageKind.WindowsService => "Windows servisi",
        UsageKind.UncApplication => "UNC yolundan çalışan uygulama",
        UsageKind.IisVirtualDirectory => "IIS sanal dizin",
        UsageKind.IisAppPool => "IIS uygulama havuzu",
        _ => "Diğer"
    };

    /// <summary>Turkish label of a database engine.</summary>
    public static string EngineLabel(DatabaseEngine engine) => engine switch
    {
        DatabaseEngine.SqlServer => "SQL Server",
        DatabaseEngine.Oracle => "Oracle",
        DatabaseEngine.Other => "Diğer",
        _ => "Bilinmiyor"
    };
}
