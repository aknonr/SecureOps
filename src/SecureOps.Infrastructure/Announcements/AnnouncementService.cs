using System.Data.Common;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Domain.Access;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Announcements;

namespace SecureOps.Infrastructure.Announcements;

/// <summary>Expected outcome of a local draft operation.</summary>
public sealed record AnnouncementOutcome(AnnouncementDraft? Draft = null, string? Html = null,
    byte[]? Email = null, string? Error = null, string[]? Fields = null,
    AnnouncementPage? Page = null, IReadOnlyList<AnnouncementBanner>? Banners = null);

/// <summary>Owner-scoped local draft orchestration; no source or mail transport dependency.</summary>
public sealed partial class AnnouncementService(SqlAnnouncementStore store, AnnouncementRenderer renderer,
    IApplicationAccessService access, IOptions<AnnouncementOptions> options, ILogger<AnnouncementService> logger)
{
    /// <summary>Revalidates persisted capabilities for every save/read/preview/download.</summary>
    public Task<AnnouncementOutcome> ExecuteAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, long version, string format, AnnouncementContent? input, int page, int pageSize, CancellationToken token) =>
        ExecuteAsync(principal, context, id, version, format, input, page, pageSize, null, token);

    internal async Task<AnnouncementOutcome> ExecuteAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, long version, string format, AnnouncementContent? input, int page, int pageSize,
        Func<AnnouncementDraft, CancellationToken, Task<string?>>? reviewedSave, CancellationToken token)
    {
        try
        {
            AccessServiceResult<EnsureAccessUserResult> current = await access.GetCurrentAsync(principal, context, token);
            if (!current.IsSuccess || current.Value!.User.Status != AccessStatus.Approved
                || !current.Value.User.Capabilities.Contains(Capabilities.AnnouncementDrafts))
            { return new(Error: "AccessDenied"); }
            if (!options.Value.Enabled)
            { return new(Error: "AnnouncementsDisabled"); }
            Guid owner = current.Value.User.Id;
            if (format == "live")
            {
                if (input is null)
                { return new(Error: "AnnouncementInvalid"); }
                string[] invalid = AnnouncementValidation.Errors(input, false);
                if (invalid.Length > 0)
                { return new(Error: "AnnouncementInvalid", Fields: invalid); }
                AnnouncementPresentation presentation = await renderer.PresentationAsync(input, token);
                var transient = new AnnouncementDraft(Guid.Empty, owner, 0, DateTimeOffset.UnixEpoch, input, "",
                    presentation.Hash, TemplateRevision: input.TemplateRevision);
                return new(Html: AnnouncementRenderer.RenderPresentation(transient, presentation, true).Html,
                    Fields: AnnouncementValidation.Errors(input, true));
            }
            if (format == "list")
            {
                if (page is < 1 or > 10000 || pageSize is < 1 or > 100)
                { return new(Error: "AnnouncementInvalid", Fields: ["Page", "PageSize"]); }
                AnnouncementPage found = await store.ListAsync(owner, page, pageSize, token);
                await store.DiscoveryAuditAsync(owner, false, found.Items.Count, context.CorrelationId, token);
                return new(Page: found with { CurrentSender = AnnouncementValidation.Address(current.Value.User.Mail) ? current.Value.User.Mail : null });
            }
            if (format is "banners" or "bundles")
            {
                IReadOnlyList<AnnouncementBanner> banners = format == "bundles" ? renderer.Bundles(token) : renderer.Banners(token);
                await store.DiscoveryAuditAsync(owner, true, banners.Count, context.CorrelationId, token);
                return new(Banners: banners);
            }
            if (id == Guid.Empty || version is < 0 or long.MaxValue || format is not ("save" or "draft" or "html" or "eml" or "prepare"))
            { return new(Error: "AnnouncementInvalid"); }
            if (format == "save")
            {
                if (input is null)
                { return new(Error: "AnnouncementInvalid"); }
                string[] errors = AnnouncementValidation.Errors(input, false);
                if (errors.Length != 0)
                { return new(Error: "AnnouncementInvalid", Fields: errors); }
                AnnouncementPresentation presentation = await renderer.PresentationAsync(input, token);
                string[] to = [.. input.To.Distinct(StringComparer.OrdinalIgnoreCase)];
                input = input with { To = to, Cc = [.. input.Cc.Except(to, StringComparer.OrdinalIgnoreCase).Distinct(StringComparer.OrdinalIgnoreCase)] };
                string sender = AnnouncementValidation.Address(current.Value.User.Mail) ? current.Value.User.Mail! : "";
                var next = new AnnouncementDraft(id, owner, version + 1, DateTimeOffset.UtcNow, input, sender,
                    presentation.Hash, TemplateRevision: input.TemplateRevision);
                string? error = reviewedSave is null ? await store.SaveAsync(next, context.CorrelationId, token)
                    : await reviewedSave(next, token);
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
            AnnouncementPresentation asset = await renderer.PresentationAsync(draft.Content, token);
            if (asset.Hash != draft.BannerHash || draft.TemplateRevision != draft.Content.TemplateRevision)
            { return new(Error: "AnnouncementAssetChanged"); }
            if (format == "html")
            {
                await store.ReadAuditAsync(draft, false, context.CorrelationId, token);
                return new(draft, AnnouncementRenderer.RenderPresentation(draft, asset, true).Html);
            }
            if (!AnnouncementValidation.Address(current.Value.User.Mail))
            { return new(Error: "AnnouncementSenderUnavailable"); }
            if (!string.Equals(draft.Sender, current.Value.User.Mail, StringComparison.Ordinal))
            { return new(Error: "AnnouncementSenderChanged"); }
            byte[] email = await AnnouncementRenderer.EmailAsync(draft, asset, token);
            if (format != "prepare")
            { await store.ReadAuditAsync(draft, true, context.CorrelationId, token); }
            return new(draft, Html: format == "prepare" ? AnnouncementRenderer.RenderPresentation(draft, asset, true).Html : null, Email: email);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            logger.LogWarning("Announcement asset missing. FailureType: {FailureType}", exception.GetType().Name);
            return new(Error: "AnnouncementAssetMissing");
        }
        catch (Exception exception) when (exception is DbException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            logger.LogError("Announcement operation failed. FailureType: {FailureType}", exception.GetType().Name);
            return new(Error: "AnnouncementUnavailable");
        }
    }
}
