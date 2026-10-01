using System.Data.Common;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Commands;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.OperationalRecords;

namespace SecureOps.Api.Controllers;

/// <summary>Record-scoped command/outcome evidence, not an employee activity report or execution endpoint.</summary>
[ApiController, Route("api/v1/operations"), Authorize]
public sealed class OperationHistoryController(IApplicationAccessService access, SqlOperationHistory history) : ControllerBase
{
    /// <summary>Latest 100 immutable events; OCO stays owner-only, other records require their view capability.</summary>
    [HttpGet("{recordType}/{recordId:guid}/events")]
    [ProducesResponseType(typeof(OperationHistoryResponse), 200)]
    public async Task<IActionResult> GetAsync(string recordType, Guid recordId, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        string? capability = recordType switch { "InUse" => Capabilities.InUseView, "OperationalRecord" => Capabilities.OperationalRecordsView, "Announcement" => Capabilities.AnnouncementDrafts, _ => null };
        if (capability is null)
        { return NotFound(); }
        AccessServiceResult<EnsureAccessUserResult> current = await access.GetCurrentAsync(User, new("operation-history", HttpContext.TraceIdentifier, null), token);
        if (!current.IsSuccess || current.Value!.User.Status != AccessStatus.Approved || !current.Value.User.Capabilities.Contains(capability))
        { return Forbid(); }
        try
        {
            OperationHistoryResponse? result = await history.ReadAsync(recordType, recordId, current.Value.User.Id, HttpContext.TraceIdentifier, token);
            return result is null ? NotFound() : Ok(result);
        }
        catch (Exception exception) when (exception is DbException or JsonException)
        { return StatusCode(503, new ProblemDetails { Status = 503, Title = "Operational evidence unavailable.", Extensions = { ["code"] = "PersistenceUnavailable" } }); }
    }
}
