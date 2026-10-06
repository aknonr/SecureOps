using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>Where a next step is worked on: the index of the account detail tab.</summary>
public enum SaTab
{
    /// <summary>Requests and actions.</summary>
    Work = 0,
    /// <summary>Usage and rule recommendation.</summary>
    Usage = 1,
    /// <summary>Usage scans.</summary>
    Scan = 2,
    /// <summary>Ownership.</summary>
    Ownership = 3,
    /// <summary>Handover and gMSA.</summary>
    Handover = 6
}

/// <summary>How a next step reads: needs a decision now, is only information, or is blocked on someone else.</summary>
public enum SaNextStepKind
{
    /// <summary>The caller may act on it now.</summary>
    Act,
    /// <summary>Waiting for someone with another responsibility; the caller cannot act.</summary>
    Wait,
    /// <summary>Nothing recorded; the data is missing (never "not used").</summary>
    Unknown
}

/// <summary>One suggested next step with the reason taken from recorded data.</summary>
public sealed record SaNextStep(string Code, string Title, string Reason, SaTab Tab, SaNextStepKind Kind);

/// <summary>
/// Orders what is open on one account from the already loaded detail (UI only; no new data, no write). The server still
/// decides every command; a step the caller may not take is shown as waiting for someone else. An empty list never means
/// the account is finished: it means this screen knows of nothing pending.
/// </summary>
public static class ServiceAccountNextStep
{
    /// <summary>Open steps, most important first.</summary>
    public static IReadOnlyList<SaNextStep> Compute(AccountDetail detail)
    {
        List<SaNextStep> steps = [];
        AccountPermissions can = detail.Permissions;
        bool mayAssign = can.AssignPerson || can.AssignTeam;

        int proposed = detail.Ownership.Count(o => o.State == "Proposed");
        if (proposed > 0)
        {
            steps.Add(mayAssign
                ? new("ownership-decide", "Sahiplik önerisini karara bağlayın", $"{proposed} sahiplik önerisi teyit bekliyor.", SaTab.Ownership, SaNextStepKind.Act)
                : new("ownership-wait", "Sahiplik önerisi teyit bekliyor", $"{proposed} öneri var; teyit yetkisi bu hesaptan sorumlu tarafta.", SaTab.Ownership, SaNextStepKind.Wait));
        }
        else if (detail.Summary.OwnerTeam is null)
        {
            steps.Add(mayAssign
                ? new("ownership-missing", "Sahip ekibi belirleyin", "Teyitli sahip ekip yok ve bekleyen bir öneri de yok.", SaTab.Ownership, SaNextStepKind.Act)
                : new("ownership-missing-wait", "Sahip ekip teyitli değil", "Sahiplik atamasını hesaptan sorumlu taraf yapar.", SaTab.Ownership, SaNextStepKind.Wait));
        }

        int overdue = detail.Requests.Count(r => r.Status == "Open" && r.Overdue);
        if (overdue > 0)
        {
            steps.Add(new("requests-overdue", "Plan bitişi geçen işleri güncelleyin", $"{overdue} açık işin plan bitişi geçti.", SaTab.Work,
                can.CanWorkAnyRequest ? SaNextStepKind.Act : SaNextStepKind.Wait));
        }

        // Server total across every attached scan (the detail carries only the first page of scans and items).
        int pendingScan = detail.UsageScans is null ? 0 : detail.UsageScanPending;
        if (pendingScan > 0)
        {
            steps.Add(new("scan-decide", "Tarama eşleşmelerine karar verin", $"{pendingScan} eşleşme kullanım kaydına alınmayı veya gerekçeli bırakılmayı bekliyor.",
                SaTab.Scan, can.Work ? SaNextStepKind.Act : SaNextStepKind.Wait));
        }

        int unverified = detail.Actions.Count(a => a.Result == nameof(ServiceAccountActionResult.Performed) && !a.Voided);
        if (unverified > 0)
        {
            steps.Add(new("actions-verify", "Bildirilen işlemleri doğrulayın", $"{unverified} işlem yapıldı olarak bildirildi, doğrulanmadı.", SaTab.Work,
                can.Verify ? SaNextStepKind.Act : SaNextStepKind.Wait));
        }

        if (detail.Rule is { } rule)
        {
            if (rule.Conformance == nameof(RuleConformance.Unplanned))
            {
                steps.Add(new("rule-unplanned", "Kurala aykırı kullanımı planlayın", "Kullanım kaydı, planlı veya tamamlanmış bir işle karşılanmıyor.", SaTab.Usage,
                    can.Work ? SaNextStepKind.Act : SaNextStepKind.Wait));
            }
            else if (rule.Conformance is nameof(RuleConformance.ManualReviewPending) or nameof(RuleConformance.IncompleteInformation))
            {
                steps.Add(new("rule-review", rule.Conformance == nameof(RuleConformance.ManualReviewPending) ? "Kural yolunu elle gözden geçirin" : "Kullanım bilgisini tamamlayın",
                    rule.ConformanceLabel, SaTab.Usage, can.Work ? SaNextStepKind.Act : SaNextStepKind.Wait));
            }
        }

        int incoming = detail.Handovers.Count(h => h.Status == nameof(HandoverStatus.Proposed));
        if (incoming > 0)
        {
            steps.Add(new("handover-decide", "Devir önerisini karara bağlayın", $"{incoming} devir önerisi bekliyor.", SaTab.Handover,
                can.DecideHandover ? SaNextStepKind.Act : SaNextStepKind.Wait));
        }

        if (!(detail.Usages ?? []).Any(u => !u.Removed) && detail.UsageScanTotal == 0)
        {
            steps.Add(new("usage-unknown", "Kullanımı belirleyin", "Kayıtlı kullanım ve tarama yok. Bu, hesabın kullanılmadığı anlamına gelmez; bilgi eksik.",
                SaTab.Usage, can.Work ? SaNextStepKind.Unknown : SaNextStepKind.Wait));
        }

        return steps;
    }
}
