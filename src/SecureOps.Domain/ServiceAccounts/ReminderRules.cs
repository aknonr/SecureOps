namespace SecureOps.Domain.ServiceAccounts;

/// <summary>Reminder channel. Outbound mail is intentionally absent.</summary>
public enum ReminderChannel
{
    /// <summary>In-app notification.</summary>
    InApp,
    /// <summary>Message draft prepared for the coordinator; never sent by the application.</summary>
    Draft
}

/// <summary>Open-request dates the reminder rules read.</summary>
/// <param name="RequestId">Request.</param>
/// <param name="AccountId">Account.</param>
/// <param name="AccountName">Account label for messages.</param>
/// <param name="ActionType">Expected action.</param>
/// <param name="TargetTeamId">Target team (in-app visibility follows scope).</param>
/// <param name="PlanEnd">Plan end.</param>
/// <param name="NextFollowupOn">Next follow-up date.</param>
/// <param name="FirstSentOn">First sent date.</param>
/// <param name="LastReplyOn">Last reply date.</param>
public sealed record ReminderInput(Guid RequestId, Guid AccountId, string AccountName, ServiceAccountActionType ActionType, Guid? TargetTeamId,
    DateOnly? PlanEnd, DateOnly? NextFollowupOn, DateOnly? FirstSentOn, DateOnly? LastReplyOn);

/// <summary>One due reminder.</summary>
/// <param name="RuleCode">Rule.</param>
/// <param name="DueDate">Deterministic due date (part of the idempotency key).</param>
/// <param name="Channel">Channel.</param>
/// <param name="Message">Turkish operator message.</param>
public sealed record DueReminder(string RuleCode, DateOnly DueDate, ReminderChannel Channel, string Message);

/// <summary>Configured calendar-day periods. Business-day rules stay off without an approved holiday calendar.</summary>
/// <param name="PlanEndLeadDays">Days before plan end, or null to disable.</param>
/// <param name="NoReplyAfterDays">Days after first send without reply, or null to disable.</param>
public sealed record ReminderSettings(int? PlanEndLeadDays, int? NoReplyAfterDays);

/// <summary>Deterministic calendar-day reminder rules; no SLA or holiday is invented.</summary>
public static class ReminderRules
{
    /// <summary>Follow-up date reached.</summary>
    public const string FollowupDue = "FollowupDue";
    /// <summary>Plan end approaching.</summary>
    public const string PlanEndSoon = "PlanEndSoon";
    /// <summary>Plan end passed while open.</summary>
    public const string PlanOverdue = "PlanOverdue";
    /// <summary>No reply recorded after first send.</summary>
    public const string NoReply = "NoReply";

    /// <summary>Turkish rule label.</summary>
    public static string Label(string rule) => rule switch
    {
        FollowupDue => "Takip tarihi geldi",
        PlanEndSoon => "Plan bitişi yaklaşıyor",
        PlanOverdue => "Plan bitişi geçti",
        NoReply => "Yanıt bekleniyor",
        _ => rule
    };

    /// <summary>Evaluates one open request for the given Istanbul business date.</summary>
    public static IReadOnlyList<DueReminder> Evaluate(ReminderInput request, DateOnly today, ReminderSettings settings)
    {
        List<DueReminder> due = [];
        string action = ServiceAccountLabels.Action(request.ActionType);
        if (request.NextFollowupOn is { } followup && followup <= today)
        {
            string message = $"{request.AccountName}: {action} talebi için takip tarihi ({followup:dd.MM.yyyy}) geldi.";
            due.Add(new DueReminder(FollowupDue, followup, ReminderChannel.InApp, message));
            due.Add(new DueReminder(FollowupDue, followup, ReminderChannel.Draft,
                $"Merhaba, {request.AccountName} servis hesabı için bekleyen \"{action}\" işiyle ilgili güncel durumu paylaşabilir misiniz? (Taslak — gönderim uygulama dışında yapılır.)"));
        }

        if (request.PlanEnd is { } end)
        {
            if (end < today)
            {
                due.Add(new DueReminder(PlanOverdue, end.AddDays(1), ReminderChannel.InApp,
                    $"{request.AccountName}: {action} planının bitişi ({end:dd.MM.yyyy}) geçti; talep açık."));
                due.Add(new DueReminder(PlanOverdue, end.AddDays(1), ReminderChannel.Draft,
                    $"Merhaba, {request.AccountName} için {end:dd.MM.yyyy} tarihinde bitmesi planlanan \"{action}\" işi hâlâ açık görünüyor. Gerçekleştiyse tarih ve kanıtı, değiştiyse yeni planı iletebilir misiniz? (Taslak)"));
            }
            else if (settings.PlanEndLeadDays is { } lead && end.AddDays(-lead) <= today)
            {
                due.Add(new DueReminder(PlanEndSoon, end.AddDays(-lead), ReminderChannel.InApp,
                    $"{request.AccountName}: {action} planının bitişine {end.DayNumber - today.DayNumber} takvim günü kaldı ({end:dd.MM.yyyy})."));
            }
        }

        if (settings.NoReplyAfterDays is { } days && request.FirstSentOn is { } sent && (request.LastReplyOn is null || request.LastReplyOn < sent)
            && sent.AddDays(days) <= today)
        {
            due.Add(new DueReminder(NoReply, sent.AddDays(days), ReminderChannel.Draft,
                $"Merhaba, {sent:dd.MM.yyyy} tarihli talebimizle ilgili ({request.AccountName}, {action}) dönüşünüzü bekliyoruz. (Taslak)"));
        }

        return due;
    }
}
