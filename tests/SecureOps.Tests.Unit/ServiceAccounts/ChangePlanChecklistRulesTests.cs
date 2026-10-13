using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Tests.Unit.ServiceAccounts;

/// <summary>Pure checklist and completion rules of the gMSA conversion change plan (CHANGE-PLAN-DESIGN.md PR 2, T9–T11).</summary>
public sealed class ChangePlanChecklistRulesTests
{
    private const ChangePlanCheckState _done = ChangePlanCheckState.Done;

    [Fact]
    public void AccountOutcome_PerformedOnlyWithDoneAndNoFailure()
    {
        ChangePlanRules.AccountOutcome([_done]).Should().Be(ChangePlanAccountOutcome.Performed);
        ChangePlanRules.AccountOutcome([_done, ChangePlanCheckState.Skipped]).Should().Be(ChangePlanAccountOutcome.Performed,
            "a skipped row (e.g. a component handled elsewhere) does not undo the done ones");
    }

    [Theory]
    [InlineData(ChangePlanCheckState.Failed)]
    [InlineData(ChangePlanCheckState.RolledBack)]
    public void AccountOutcome_FailedOrRolledBackIsNotPerformed(ChangePlanCheckState bad) =>
        ChangePlanRules.AccountOutcome([_done, bad]).Should().Be(ChangePlanAccountOutcome.NotPerformed);

    [Fact]
    public void AccountOutcome_NothingDoneIsNotPerformed()
    {
        ChangePlanRules.AccountOutcome([ChangePlanCheckState.Skipped, ChangePlanCheckState.Skipped]).Should().Be(ChangePlanAccountOutcome.NotPerformed);
        ChangePlanRules.AccountOutcome([null]).Should().Be(ChangePlanAccountOutcome.NotPerformed);
        ChangePlanRules.AccountOutcome([]).Should().Be(ChangePlanAccountOutcome.NotPerformed);
    }

    [Fact]
    public void RollBack_NeedsDoneOrFailed()
    {
        ChangePlanRules.CanRollBack(_done).Should().BeTrue();
        ChangePlanRules.CanRollBack(ChangePlanCheckState.Failed).Should().BeTrue();
        ChangePlanRules.CanRollBack(null).Should().BeFalse("nothing was marked yet");
        ChangePlanRules.CanRollBack(ChangePlanCheckState.Skipped).Should().BeFalse();
        ChangePlanRules.CanRollBack(ChangePlanCheckState.RolledBack).Should().BeFalse();
    }

    [Fact]
    public void OutsideWindow_BothEndsAreInside()
    {
        DateTimeOffset start = new(2026, 10, 12, 22, 0, 0, TimeSpan.Zero), end = start.AddHours(4);
        ChangePlanRules.IsOutsideWindow(start, start, end).Should().BeFalse();
        ChangePlanRules.IsOutsideWindow(end, start, end).Should().BeFalse();
        ChangePlanRules.IsOutsideWindow(start.AddTicks(-1), start, end).Should().BeTrue();
        ChangePlanRules.IsOutsideWindow(end.AddTicks(1), start, end).Should().BeTrue();
    }

    [Theory]
    [InlineData("Done", null, null)]
    [InlineData("Done", "Sentetik not", null)]
    [InlineData("Skipped", null, "note")]
    [InlineData("Failed", "   ", "note")]
    [InlineData("RolledBack", "Sentetik geri alma", null)]
    [InlineData("done", null, "state")]
    [InlineData("0", null, "state")]
    [InlineData("Verified", "x", "state")]
    [InlineData(null, null, "state")]
    public void CheckInput_ExactStates_NoteRequiredExceptDone(string? state, string? note, string? expected) =>
        ChangePlanRules.CheckInputError(state, note).Should().Be(expected);

    [Fact]
    public void CheckInput_NoteIsBounded() =>
        ChangePlanRules.CheckInputError("Failed", new string('x', ChangePlanRules.MaxReason + 1)).Should().Be("note");

    [Fact]
    public void Labels_SayDoneIsAStatement_AndNeverVerified()
    {
        ChangePlanRules.CheckStateLabel(_done).Should().Contain("beyan").And.Contain("doğrulama değil");
        ChangePlanRules.OutcomeLabel(ChangePlanAccountOutcome.Performed).Should().Contain("doğrulama ayrı");
        Enum.GetValues<ChangePlanCheckState>().Select(ChangePlanRules.CheckStateLabel).Should().OnlyHaveUniqueItems();
    }
}
