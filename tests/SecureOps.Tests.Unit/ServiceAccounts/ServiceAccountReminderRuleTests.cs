using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Tests.Unit.ServiceAccounts;

public sealed class ServiceAccountReminderRuleTests
{
    private static readonly DateOnly _today = new(2026, 9, 28);
    private static readonly ReminderSettings _settings = new(2, 7);

    [Fact]
    public void DueDatesAreDeterministic_SoRepeatedEvaluationsProduceTheSameKeys()
    {
        ReminderInput request = Request(planEnd: _today.AddDays(-3), followup: _today.AddDays(-1), sent: _today.AddDays(-10));
        IReadOnlyList<DueReminder> first = ReminderRules.Evaluate(request, _today, _settings);
        IReadOnlyList<DueReminder> later = ReminderRules.Evaluate(request, _today.AddDays(4), _settings);
        first.Select(d => (d.RuleCode, d.DueDate, d.Channel)).Should().BeEquivalentTo(later.Select(d => (d.RuleCode, d.DueDate, d.Channel)));
        first.Should().Contain(d => d.RuleCode == ReminderRules.PlanOverdue && d.DueDate == _today.AddDays(-2) && d.Channel == ReminderChannel.InApp);
        first.Should().Contain(d => d.RuleCode == ReminderRules.FollowupDue && d.Channel == ReminderChannel.Draft);
        first.Should().Contain(d => d.RuleCode == ReminderRules.NoReply && d.DueDate == _today.AddDays(-3));
        first.Should().HaveCount(5);
    }

    [Fact]
    public void PlanEndSoon_UsesConfiguredCalendarDays_AndDisabledRulesStayOff()
    {
        ReminderInput request = Request(planEnd: _today.AddDays(2), followup: null, sent: _today.AddDays(-3));
        ReminderRules.Evaluate(request, _today, _settings).Should().ContainSingle()
            .Which.Should().Match<DueReminder>(d => d.RuleCode == ReminderRules.PlanEndSoon && d.DueDate == _today && d.Message.Contains("2 takvim günü"));
        ReminderRules.Evaluate(request, _today.AddDays(-1), _settings).Should().BeEmpty();
        ReminderRules.Evaluate(Request(_today.AddDays(1), null, _today.AddDays(-30)), _today, new ReminderSettings(null, null)).Should().BeEmpty();
    }

    [Fact]
    public void ReplyAfterSend_StopsNoReply_ButReplyBeforeSendDoesNot()
    {
        ReminderRules.Evaluate(Request(null, null, _today.AddDays(-9), reply: _today.AddDays(-2)), _today, _settings).Should().BeEmpty();
        ReminderRules.Evaluate(Request(null, null, _today.AddDays(-9), reply: _today.AddDays(-20)), _today, _settings)
            .Should().ContainSingle(d => d.RuleCode == ReminderRules.NoReply && d.Channel == ReminderChannel.Draft);
    }

    [Fact]
    public void Messages_AreTurkish_AndDraftsSayTheyAreNotSent()
    {
        IReadOnlyList<DueReminder> due = ReminderRules.Evaluate(Request(_today.AddDays(-1), _today, _today.AddDays(-8)), _today, _settings);
        due.Where(d => d.Channel == ReminderChannel.Draft).Should().OnlyContain(d => d.Message.Contains("Taslak"));
        due.Should().OnlyContain(d => d.Message.Contains("SYN_SVC"));
        ReminderRules.Label(ReminderRules.NoReply).Should().Be("Yanıt bekleniyor");
    }

    private static ReminderInput Request(DateOnly? planEnd, DateOnly? followup, DateOnly? sent, DateOnly? reply = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "SYN_SVC", ServiceAccountActionType.PasswordChange, null, planEnd, followup, sent, reply);
}
