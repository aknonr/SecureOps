using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Api.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Api.Controllers.ServiceAccounts;

/// <summary>
/// Field suggestions from a pasted reply mail (EVENT-GUIDANCE-DESIGN §7). Deterministic rules, no language model, nothing written and
/// nothing stored: the text is not logged, audited or returned.
/// </summary>
[ApiController]
[Route("api/v1/service-accounts")]
[Authorize(Policy = ServiceAccountPolicies.Work)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class ServiceAccountMailHintsController(ServiceAccountService service) : ControllerBase
{
    // 20 000 characters at up to 4 UTF-8 bytes each, plus JSON escaping headroom.
    private const long _maxRequestBytes = 128 * 1024;

    /// <summary>Suggests event kind, record numbers and dates from the pasted text; read only, the text is never kept.</summary>
    [HttpPost("accounts/{id:guid}/events/mail-hints")]
    [Consumes("application/json")]
    [RequestSizeLimit(_maxRequestBytes)]
    [ProducesResponseType(typeof(MailHints), StatusCodes.Status200OK)]
    public async Task<ActionResult<MailHints>> MailHintsAsync(Guid id, MailHintsRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.MailHintsAsync(User, ServiceAccountReplies.Context(this), id, request, cancellationToken));
}
