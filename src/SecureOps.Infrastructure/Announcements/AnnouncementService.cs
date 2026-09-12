using System.Data.Common;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Domain.Access;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Announcements;

/// <summary>Expected outcome of a local draft operation.</summary>
public sealed record AnnouncementOutcome(AnnouncementDraft? Draft = null, string? Html = null,
    byte[]? Email = null, string? Error = null, string[]? Fields = null);

/// <summary>Owner-scoped local draft orchestration; no source or mail transport dependency.</summary>
public sealed class AnnouncementService(SqlAnnouncementStore store, AnnouncementRenderer renderer,
    IApplicationAccessService access, IOptions<AnnouncementOptions> options, ILogger<AnnouncementService> logger)
{
    /// <summary>Revalidates persisted capabilities for every save/read/preview/download.</summary>
    public async Task<AnnouncementOutcome> ExecuteAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, long version, string format, AnnouncementContent? input, CancellationToken token)
    {
        try
        {
            AccessServiceResult<EnsureAccessUserResult> current = await access.GetCurrentAsync(principal, context, token);
            if (!current.IsSuccess || current.Value!.User.Status != AccessStatus.Approved
                || !current.Value.User.Capabilities.Contains(Capabilities.AnnouncementDrafts))
            { return new(Error: "AccessDenied"); }
            if (!options.Value.Enabled)
            { return new(Error: "AnnouncementsDisabled"); }
            if (id == Guid.Empty || version is < 0 or long.MaxValue || format is not ("save" or "draft" or "html" or "eml"))
            { return new(Error: "AnnouncementInvalid"); }
            Guid owner = current.Value.User.Id;
            if (format == "save")
            {
                if (input is null)
                { return new(Error: "AnnouncementInvalid"); }
                string[] errors = AnnouncementValidation.Errors(input, false);
                if (errors.Length != 0)
                { return new(Error: "AnnouncementInvalid", Fields: errors); }
                if (!AnnouncementValidation.Address(options.Value.Sender))
                { return new(Error: "AnnouncementConfigurationUnavailable"); }
                (byte[] _, string _, string hash) = await renderer.AssetAsync(input.BannerRevision, token);
                string[] to = [.. input.To.Distinct(StringComparer.OrdinalIgnoreCase)];
                input = input with { To = to, Cc = [.. input.Cc.Except(to, StringComparer.OrdinalIgnoreCase).Distinct(StringComparer.OrdinalIgnoreCase)] };
                var next = new AnnouncementDraft(id, owner, version + 1, DateTimeOffset.UtcNow, input, options.Value.Sender, hash);
                string? error = await store.SaveAsync(next, context.CorrelationId, token);
                return error is null ? new(next) : new(Error: error);
            }
            AnnouncementDraft? draft = await store.GetAsync(id, owner, token);
            if (draft is null)
            { return new(Error: "AnnouncementNotFound"); }
            if (version != 0 && version != draft.Version || format != "draft" && version == 0)
            { return new(Error: "AnnouncementConflict"); }
            if (format == "draft")
            {
                await store.ReadAuditAsync(draft, false, context.CorrelationId, token);
                return new(draft);
            }
            string[] missing = AnnouncementValidation.Errors(draft.Content, true);
            if (missing.Length != 0)
            { return new(Error: "AnnouncementIncomplete", Fields: missing); }
            (byte[] Bytes, string Type, string Hash) asset = await renderer.AssetAsync(draft.Content.BannerRevision, token);
            if (asset.Hash != draft.BannerHash || draft.TemplateRevision != "oco-v1")
            { return new(Error: "AnnouncementAssetChanged"); }
            if (format == "html")
            {
                await store.ReadAuditAsync(draft, false, context.CorrelationId, token);
                return new(draft, AnnouncementRenderer.Render(draft, $"data:image/{asset.Type};base64,{Convert.ToBase64String(asset.Bytes)}").Html);
            }
            byte[] email = await AnnouncementRenderer.EmailAsync(draft, asset.Bytes, asset.Type, token);
            await store.ReadAuditAsync(draft, true, context.CorrelationId, token);
            return new(draft, Email: email);
        }
        catch (Exception exception) when (exception is DbException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            logger.LogError("Announcement operation failed. FailureType: {FailureType}", exception.GetType().Name);
            return new(Error: "AnnouncementUnavailable");
        }
    }
}
