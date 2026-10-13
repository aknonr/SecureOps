using System.Security.Claims;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class ServiceAccountService
{
    /// <summary>
    /// Field suggestions from a pasted reply (EVENT-GUIDANCE-DESIGN §7): deterministic rules, no language model, nothing written, nothing
    /// stored. Needs the Work capability and an account inside the caller's scope (out of scope or unknown = 404, as the detail). The text
    /// is passed to the pure rule function and nowhere else: it is not logged, audited, cached or echoed, and this method logs nothing.
    /// </summary>
    public Task<SaResult<MailHints>> MailHintsAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid accountId, MailHintsRequest? request,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Work, async caller =>
        {
            if (!await AccountVisibleAsync(caller, accountId, cancellationToken))
            {
                return SaResult<MailHints>.Fail(SaErrors.NotFound);
            }

            // Empty and over-long input are refused before any matching (the field name is the only thing reported).
            if (string.IsNullOrWhiteSpace(request?.Text) || EventMailHints.Analyze(request.Text, Today) is not { } result)
            {
                return SaResult<MailHints>.Fail(SaErrors.Invalid, "text");
            }

            return new SaResult<MailHints>(new MailHints(result.RuleSet,
                [.. result.Events.Select(e => new MailHintEvent(e.EventKind, e.Reason))],
                [.. result.References.Select(r => new MailHintReference(r.Type, r.Number, r.Rule))],
                [.. result.Dates.Select(d => new MailHintDate(d.Value, d.Kind, d.Rule))],
                result.Warnings));
        }, cancellationToken);
}
