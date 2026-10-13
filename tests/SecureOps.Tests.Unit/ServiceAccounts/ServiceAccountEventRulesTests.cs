using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Unit.ServiceAccounts;

/// <summary>OLAY-2c: the event → record table (EVENT-GUIDANCE-DESIGN.md §3) and the draft contract (§5.2, threats E1/E2).</summary>
public sealed class ServiceAccountEventRulesTests
{
    private static readonly JsonSerializerOptions _web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Table_MatchesTheDesign_RowByRow()
    {
        AccountEventShape S(AccountEventKind kind) => ServiceAccountEventRules.Shape(kind);

        S(AccountEventKind.OwnerReplied).Should().Be(new AccountEventShape(AccountEventKind.OwnerReplied, EventBlockRule.Required, CommunicationDirection.Incoming,
            CommunicationKind.Reply, null, null, true, true, null, false, true));
        S(AccountEventKind.PasswordChanged).Should().Be(new AccountEventShape(AccountEventKind.PasswordChanged, EventBlockRule.Optional, CommunicationDirection.Incoming,
            CommunicationKind.Information, ServiceAccountActionType.PasswordChange, ServiceAccountRecordKind.Intermediate, true, true, null, false, false));
        S(AccountEventKind.AccountDeleted).Should().Be(new AccountEventShape(AccountEventKind.AccountDeleted, EventBlockRule.Optional, null, null,
            ServiceAccountActionType.Deletion, ServiceAccountRecordKind.Closure, true, true, null, false, false));
        S(AccountEventKind.GmsaConverted).Should().Be(new AccountEventShape(AccountEventKind.GmsaConverted, EventBlockRule.Optional, null, null,
            ServiceAccountActionType.GmsaConversion, ServiceAccountRecordKind.Closure, true, true, null, false, false));
        S(AccountEventKind.OwnerChanged).Should().Be(new AccountEventShape(AccountEventKind.OwnerChanged, EventBlockRule.Optional, null, null,
            null, null, false, false, null, true, false));
        S(AccountEventKind.ServiceMoved).Should().Be(new AccountEventShape(AccountEventKind.ServiceMoved, EventBlockRule.Required, null, null,
            null, null, true, false, ServiceAccountActionType.Review, false, false));
        S(AccountEventKind.Other).Should().Be(new AccountEventShape(AccountEventKind.Other, EventBlockRule.Required, null, null,
            null, null, true, false, ServiceAccountActionType.Evaluate, false, false));
    }

    [Fact]
    public void NoEvent_VerifiesOrClosesWithAWrongKind()
    {
        // E1: an event only reports; E2: a closure candidate only where the account can actually be retired.
        ServiceAccountEventRules.ActionResult.Should().Be(ServiceAccountActionResult.Performed);
        foreach (AccountEventKind kind in Enum.GetValues<AccountEventKind>())
        {
            AccountEventShape shape = ServiceAccountEventRules.Shape(kind);
            shape.Kind.Should().Be(kind);
            (shape.ActionType is null).Should().Be(shape.RecordKind is null, kind.ToString());
            if (shape.ActionType is { } type)
            {
                ServiceAccountRules.ValidateRecordKind(type, shape.RecordKind!.Value).Should().BeNull(kind.ToString());
                (shape.RecordKind == ServiceAccountRecordKind.Closure).Should().Be(ServiceAccountRules.CanCloseAccount(type), kind.ToString());
            }

            // A request is linked only where an action may count for it, and only one of link/create is possible.
            (shape.LinkRequest && shape.CreateRequestType is not null).Should().BeFalse(kind.ToString());
        }

        Action unknown = () => ServiceAccountEventRules.Shape((AccountEventKind)99);
        unknown.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RequestAccepts_IsTheCloseRuleMatch()
    {
        // Ownership confirmation requests close on the confirmed owner, not on an action, so the close rule cannot be the oracle there.
        foreach (ServiceAccountActionType request in Enum.GetValues<ServiceAccountActionType>().Where(t => t != ServiceAccountActionType.OwnershipConfirmation))
        {
            foreach (ServiceAccountActionType action in Enum.GetValues<ServiceAccountActionType>())
            {
                bool counts = ServiceAccountRules.EvaluateClose(ServiceAccountRequestStatus.Open, request, ServiceAccountCloseOutcome.Completed, null,
                    [new ActionFacts(action, ServiceAccountActionResult.Performed, ServiceAccountRecordKind.Intermediate, new(2026, 9, 1), null, false, false, false, false)])
                    .All(g => g.Code != ServiceAccountRules.Errors.NoMatchingAction);
                ServiceAccountRules.RequestAccepts(request, action).Should().Be(counts, $"{request} ← {action}");
            }
        }

        ServiceAccountRules.RequestAccepts(ServiceAccountActionType.OwnershipConfirmation, ServiceAccountActionType.PasswordChange).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"eventKind":"PasswordChanged","occurredOn":"2026-10-01","datePrecision":"DateOnly","channel":"Mail","accountVersion":"x","result":"Verified"}""")]
    [InlineData("""{"eventKind":"PasswordChanged","occurredOn":"2026-10-01","datePrecision":"DateOnly","channel":"Mail","accountVersion":"x","verifiedOn":"2026-10-02"}""")]
    [InlineData("""{"eventKind":"PasswordChanged","occurredOn":"2026-10-01","datePrecision":"DateOnly","channel":"Mail","accountVersion":"x","action":{"actionType":"Deletion"}}""")]
    [InlineData("""{"eventKind":"PasswordChanged","occurredOn":"2026-10-01","datePrecision":"DateOnly","channel":"Mail","accountVersion":"x","action":{"recordKind":"Closure"}}""")]
    [InlineData("""{"eventKind":"PasswordChanged","occurredOn":"2026-10-01","datePrecision":"DateOnly","channel":"Mail","accountVersion":"x","action":{"verifierPersonId":"00000000-0000-0000-0000-000000000001"}}""")]
    [InlineData("""{"eventKind":"OwnerChanged","occurredOn":null,"datePrecision":"Unknown","channel":"Mail","accountVersion":"x","ownership":{"teamId":null,"personId":null,"reason":"r","mode":"Confirm"}}""")]
    [InlineData("""{"eventKind":"Other","occurredOn":null,"datePrecision":"Unknown","channel":"Mail","accountVersion":"x","communication":{"body":"mail metni"}}""")]
    [InlineData("""{"eventKind":"Other","occurredOn":null,"datePrecision":"Unknown","channel":"Mail","accountVersion":"x","closeRequest":{"outcome":"Completed","reason":"x"}}""")]
    public void Draft_RefusesEveryMemberOutsideTheSchema(string json)
    {
        // E1/E2: a verification, a record kind, an action type, an ownership decision or a mail body cannot even be bound.
        Action bind = () => JsonSerializer.Deserialize<EventDraft>(json, _web);
        bind.Should().Throw<JsonException>();
    }

    [Fact]
    public void Draft_BindsTheDocumentedShape()
    {
        EventDraft draft = JsonSerializer.Deserialize<EventDraft>("""
            {"eventKind":"PasswordChanged","occurredOn":"2026-10-01","datePrecision":"DateOnly","channel":"Mail","accountVersion":"AAAAAAAAB9E=",
             "requestId":"00000000-0000-0000-0000-000000000002","closeRequest":{"outcome":"Completed"},
             "communication":{"summary":"kısa not","providerMessageId":"<syn@example>"},"action":{"evidenceNote":"OCO ile yapıldı"},
             "references":[{"type":"OCO","number":"OCO-00000"}],"idempotencyKey":"00000000-0000-0000-0000-000000000003","previewDigest":"ab"}
            """, _web)!;
        draft.EventKind.Should().Be("PasswordChanged");
        draft.CloseRequest!.Outcome.Should().Be("Completed");
        draft.References.Should().ContainSingle().Which.Number.Should().Be("OCO-00000");
        draft.IdempotencyKey.Should().NotBeNull();
    }

    [Fact]
    public void EventContracts_CarryNoVerificationOrBodyMember()
    {
        Type[] types = [typeof(EventDraft), typeof(EventCreateRequest), typeof(EventCloseRequest), typeof(EventCommunication), typeof(EventAction), typeof(EventOwnership)];
        string[] members = [.. types.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)).Select(p => p.Name)];
        members.Should().NotContain(m => m.Contains("Verif", StringComparison.Ordinal) || m == "Result" || m == "RecordKind" || m == "Mode"
            || m.Contains("Body", StringComparison.Ordinal) || m.Contains("Password", StringComparison.Ordinal) || m == "Text");
        typeof(EventAction).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(["PerformerTeamId", "PerformerPersonId", "EvidenceNote"]);
    }
}
