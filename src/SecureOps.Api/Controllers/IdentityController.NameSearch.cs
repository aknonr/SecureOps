using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SecureOps.Api.Middleware;
using SecureOps.Api.Security;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Api.Controllers;

public sealed partial class IdentityController
{
    /// <summary>Bounded first/full-name lookup independent of Service Accounts activation or scope. No inventory links.</summary>
    [HttpPost("name-search")]
    [Authorize(Policy = Policies.CanIdentityLookup)]
    [EnableRateLimiting(ApiRateLimits.IdentityLookup)]
    [ProducesResponseType(typeof(DirectoryNameSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DirectoryNameSearchResponse>> NameSearchAsync(
        [FromBody] DirectoryNameSearchRequest? request,
        [FromServices] IDirectoryNameSearchProvider directory,
        [FromServices] DirectAuditWriter audit,
        CancellationToken cancellationToken)
    {
        string correlationId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var query = DirectoryNameQuery.TryCreate(request?.Query, out string? error);
        string? hash = AuditAccountHasher.HashAccountInput(query?.Text ?? request?.Query);
        if (query is null)
        {
            return await TryWriteNameSearchAuditAsync(audit, "Rejected", new { QueryHash = hash, Error = error }, correlationId, cancellationToken)
                ? OperationalProblemDetails.Create(StatusCodes.Status400BadRequest, error!, "The name query was rejected.", correlationId, "validation", false)
                : AuditUnavailable(correlationId);
        }

        if (!await TryWriteNameSearchAuditAsync(audit, "Requested", new { QueryHash = hash, QueryLength = query.Text.Length, Words = query.Tokens.Count },
            correlationId, cancellationToken))
        {
            return AuditUnavailable(correlationId);
        }

        DirectoryNameSearchResult found;
        try
        {
            found = await directory.SearchAsync(query, DirectoryNameQuery.MaximumResults, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            string failure = exception is TimeoutException or OperationCanceledException ? "Timeout" : "ProviderUnavailable";
            _logger.LogError("Directory name search failed. FailureType={FailureType} CorrelationId={CorrelationId}", exception.GetType().Name, correlationId);
            return await TryWriteNameSearchAuditAsync(audit, "Failed", new { QueryHash = hash, Failure = failure }, correlationId, cancellationToken)
                ? OperationalProblemDetails.Create(StatusCodes.Status503ServiceUnavailable, "IdentityProviderUnavailable",
                    "The directory name search could not be completed.", correlationId, "provider", true)
                : AuditUnavailable(correlationId);
        }

        DirectoryNameCandidate[] matches = [.. DirectoryNameMatching.Order(query, found.Candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.SamAccountName) && DirectoryNameMatching.Matches(query, c)))
            .Take(DirectoryNameQuery.MaximumResults)];
        var sameName = matches.GroupBy(m => DirectoryNameMatching.Fold(m.DisplayName ?? m.SamAccountName)).Where(g => g.Count() > 1)
            .Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        DirectoryNameMatch[] results = [.. matches.Select(m => new DirectoryNameMatch(m.DisplayName ?? m.SamAccountName, m.SamAccountName,
            m.Department, sameName.Contains(DirectoryNameMatching.Fold(m.DisplayName ?? m.SamAccountName)), null))];
        bool truncated = found.Truncated || found.Candidates.Count > DirectoryNameQuery.MaximumResults;
        return await TryWriteNameSearchAuditAsync(audit, "Completed", new { QueryHash = hash, Results = results.Length, Truncated = truncated },
            correlationId, cancellationToken)
            ? Ok(new DirectoryNameSearchResponse(results, truncated, DirectoryNameQuery.MinimumLetters, DirectoryNameQuery.MaximumResults))
            : AuditUnavailable(correlationId);
    }

    private async Task<bool> TryWriteNameSearchAuditAsync(IAuditWriter audit, string outcome, object details, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            await audit.WriteAsync(new AuditEvent
            {
                Actor = User.Identity?.Name ?? "unknown",
                Action = "Identity.DirectoryNameSearch" + outcome,
                CorrelationId = correlationId,
                SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
                Details = details
            }, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Directory name search audit failed. FailureType={FailureType} CorrelationId={CorrelationId}", exception.GetType().Name, correlationId);
            return false;
        }
    }
}
