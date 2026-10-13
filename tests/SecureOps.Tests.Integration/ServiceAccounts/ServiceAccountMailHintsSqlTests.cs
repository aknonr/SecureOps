using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>OLAY-2b: <c>events/mail-hints</c> against real SQL — Work capability, account scope, input limits, and nothing written.</summary>
public sealed class ServiceAccountMailHintsSqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;
    private static readonly string[] _all = [.. ServiceAccountCapabilities.All.Where(c => c != ServiceAccountCapabilities.Administer)];

    [ServiceAccountSqlFact]
    public async Task Hints_NeedWork_AndAnAccountInScope_ValidateInput_AndWriteNothing()
    {
        ServiceAccountSqlFixture fx = new();
        Guid org = await fx.OrganizationAsync("SYN MH ORG " + fx.Suffix);
        Guid team = await fx.TeamAsync("SYN MH EKIP " + fx.Suffix, org), otherTeam = await fx.TeamAsync("SYN MH DIGER " + fx.Suffix, null);
        SynUser worker = await fx.UserAsync(_all);
        await fx.GrantAsync(worker, ScopeKind.Organization, org);
        AccountDetail account = Ok(await fx.Service.CreateAccountAsync(worker.Principal, fx.Context,
            new CreateAccountRequest($"SYN{fx.Suffix}_MAILHINT", null, org, "Sentetik test hesabı"), _token));
        Guid id = account.Summary.Id;

        MailHints hints = Ok(await fx.Service.MailHintsAsync(worker.Principal, fx.Context, id,
            new MailHintsRequest("Parola değişti, OCO-00000 ile yapıldı, 1 Ekim 2020."), _token));
        hints.RuleSet.Should().Be("olay-1");
        hints.EventSuggestions.Should().ContainSingle(e => e.EventKind == "PasswordChanged");
        hints.References.Should().ContainSingle(r => r.Type == "OCO" && r.Number == "OCO-00000");
        hints.Dates.Should().ContainSingle(d => d.Value == new DateOnly(2020, 10, 1) && d.Kind == "Explicit");

        SynUser viewer = await fx.UserAsync(ServiceAccountCapabilities.View);
        await fx.GrantAsync(viewer, ScopeKind.Organization, org);
        (await fx.Service.MailHintsAsync(viewer.Principal, fx.Context, id, new MailHintsRequest("x"), _token)).ErrorCode
            .Should().Be(SaErrors.Forbidden, "View alone cannot ask for hints");

        SynUser outsider = await fx.UserAsync(_all);
        await fx.GrantAsync(outsider, ScopeKind.Team, team: otherTeam);
        (await fx.Service.MailHintsAsync(outsider.Principal, fx.Context, id, new MailHintsRequest("Parola değişti"), _token)).ErrorCode
            .Should().Be(SaErrors.NotFound, "out of scope and unknown are indistinguishable");
        (await fx.Service.MailHintsAsync(worker.Principal, fx.Context, Guid.NewGuid(), new MailHintsRequest("Parola değişti"), _token)).ErrorCode
            .Should().Be(SaErrors.NotFound);

        foreach (MailHintsRequest? bad in new MailHintsRequest?[] { null, new(null), new(""), new("   "), new(new string('a', EventMailHintRules.MaxTextLength + 1)) })
        {
            SaResult<MailHints> invalid = await fx.Service.MailHintsAsync(worker.Principal, fx.Context, id, bad, _token);
            invalid.ErrorCode.Should().Be(SaErrors.Invalid);
            invalid.Field.Should().Be("text");
        }

        Ok(await fx.Service.MailHintsAsync(worker.Principal, fx.Context, id, new MailHintsRequest(new string('a', EventMailHintRules.MaxTextLength)), _token))
            .Warnings.Should().Equal("NothingFound");

        object accountId = new { accountId = id };
        object actor = new { a = worker.User.Id.ToString("D") };
        int history = await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE AccountId = @accountId", accountId);
        int audit = await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Actor = @a", actor);
        Ok(await fx.Service.MailHintsAsync(worker.Principal, fx.Context, id, new MailHintsRequest("Hesap silindi OR-1"), _token));
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE AccountId = @accountId", accountId)).Should().Be(history);
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Actor = @a", actor)).Should().Be(audit, "hints are neither history nor an audited action");
        Ok(await fx.Service.AccountAsync(worker.Principal, fx.Context, id, _token)).Summary.Version.Should().Be(account.Summary.Version);
    }

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }
}
