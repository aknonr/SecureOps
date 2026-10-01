using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Api.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Api.Controllers.ServiceAccounts;

/// <summary>
/// Guided import: stage (server-held bytes and hash) → preview → versioned decisions → idempotent atomic commit.
/// No formula evaluation, macro, external link or source-system call.
/// </summary>
[ApiController]
[Route("api/v1/service-accounts/imports")]
[Authorize(Policy = ServiceAccountPolicies.Import)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class ServiceAccountImportsController(ServiceAccountService service) : ControllerBase
{
    private const long _maxRequestBytes = 26L * 1024 * 1024;

    /// <summary>Stages a file with the declared source report date, provenance and scope; returns the preview summary.</summary>
    [HttpPost]
    [RequestSizeLimit(_maxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = _maxRequestBytes)]
    [ProducesResponseType(typeof(ImportBatchView), StatusCodes.Status200OK)]
    public async Task<ActionResult<ImportBatchView>> StageAsync([FromForm] IFormFile file, [FromForm] string profile, [FromForm] DateOnly? sourceReportDate,
        [FromForm] string sourceDateProvenance, [FromForm] string? declaredScope, [FromForm] string? declaredDomain, [FromForm] string? sheet,
        [FromForm] string? targetTeam, [FromForm] string? mappingJson, [FromForm] string? coverage, [FromForm] string? coverageOrganizationIds,
        CancellationToken cancellationToken)
    {
        // Declared completeness population: comma-separated organization IDs (validated against scope by the service).
        List<Guid> coverageOrganizations = [];
        foreach (string part in (coverageOrganizationIds ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(part, out Guid organizationId))
            {
                return ServiceAccountReplies.Reply(this, SaResult<ImportBatchView>.Fail(SaErrors.Invalid, "coverageOrganizationIds"));
            }

            coverageOrganizations.Add(organizationId);
        }

        IReadOnlyList<ImportColumnMapping>? mapping = null;
        if (!string.IsNullOrWhiteSpace(mappingJson))
        {
            try
            {
                mapping = JsonSerializer.Deserialize<ImportColumnMapping[]>(mappingJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            catch (JsonException)
            {
                return ServiceAccountReplies.Reply(this, SaResult<ImportBatchView>.Fail(SaErrors.Invalid, "mapping"));
            }
        }

        if (file is null || file.Length is 0 or > _maxRequestBytes)
        {
            return ServiceAccountReplies.Reply(this, SaResult<ImportBatchView>.Fail(SaErrors.Invalid, "file"));
        }

        using MemoryStream buffer = new();
        await file.CopyToAsync(buffer, cancellationToken);
        StageImportRequest request = new(profile, sourceReportDate, sourceDateProvenance, declaredScope, declaredDomain, sheet, targetTeam, mapping,
            coverage, coverageOrganizations);
        return ServiceAccountReplies.Reply(this, await service.StageImportAsync(User, ServiceAccountReplies.Context(this), request, file.FileName,
            file.ContentType, buffer.ToArray(), cancellationToken));
    }

    /// <summary>Recent import batches.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ImportHistoryItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ImportHistoryItem>>> ListAsync(CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.ImportsAsync(User, ServiceAccountReplies.Context(this), cancellationToken));

    /// <summary>Batch state, summary, versions and (after commit) the persisted result.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ImportBatchView), StatusCodes.Status200OK)]
    public async Task<ActionResult<ImportBatchView>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.ImportAsync(User, ServiceAccountReplies.Context(this), id, cancellationToken));

    /// <summary>Preview rows with old/new values, errors and decision scope.</summary>
    [HttpGet("{id:guid}/rows")]
    [ProducesResponseType(typeof(ImportRowPage), StatusCodes.Status200OK)]
    public async Task<ActionResult<ImportRowPage>> RowsAsync(Guid id, [FromQuery] string? classification, [FromQuery] string? kind,
        [FromQuery] bool decisionsOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        ServiceAccountReplies.Reply(this, await service.ImportRowsAsync(User, ServiceAccountReplies.Context(this), id, classification, kind, decisionsOnly,
            page, pageSize, cancellationToken));

    /// <summary>Records decisions at the expected decision version (409 when stale).</summary>
    [HttpPut("{id:guid}/decisions")]
    [ProducesResponseType(typeof(ImportBatchView), StatusCodes.Status200OK)]
    public async Task<ActionResult<ImportBatchView>> DecideAsync(Guid id, ImportDecisionsRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.DecideImportAsync(User, ServiceAccountReplies.Context(this), id, request, cancellationToken));

    /// <summary>Re-runs the preview against current data.</summary>
    [HttpPost("{id:guid}/preview")]
    [ProducesResponseType(typeof(ImportBatchView), StatusCodes.Status200OK)]
    public async Task<ActionResult<ImportBatchView>> RefreshAsync(Guid id, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.RefreshImportAsync(User, ServiceAccountReplies.Context(this), id, cancellationToken));

    /// <summary>Commits atomically. Requires the Idempotency-Key header; a repeated commit returns the stored result.</summary>
    [HttpPost("{id:guid}/commit")]
    [ProducesResponseType(typeof(ImportBatchView), StatusCodes.Status200OK)]
    public async Task<ActionResult<ImportBatchView>> CommitAsync(Guid id, ImportCommitRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.CommitImportAsync(User, ServiceAccountReplies.Context(this), id, request, idempotencyKey, cancellationToken));
}
