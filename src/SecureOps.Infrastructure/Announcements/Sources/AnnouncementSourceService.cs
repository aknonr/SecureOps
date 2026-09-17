using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Domain.Access;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Announcements;

namespace SecureOps.Infrastructure.Announcements.Sources;

/// <summary>Expected outcome of a source operation; exactly one payload or one error code is set.</summary>
public sealed record AnnouncementSourceOutcome(string? Error = null, string[]? Fields = null,
    IReadOnlyList<MaintenanceProfileChoice>? Profiles = null, AnnouncementSourceJobStatus? Status = null,
    AnnouncementSourceProposal? Proposal = null, AnnouncementSourceApplyResult? Applied = null);

/// <summary>
/// Owner-scoped source orchestration. Submission only records and enqueues; no source call and no legacy
/// script ever runs inside an API request. Applying a snapshot goes through the existing draft save path,
/// so validation, versioning, provenance and audit stay exactly as the draft module defines them.
/// </summary>
public sealed class AnnouncementSourceService(SqlAnnouncementStore drafts, SqlAnnouncementSourceStore store,
    AnnouncementService saves, MaintenanceProfileCatalog profiles, AnnouncementSourceRecovery recovery,
    IApplicationAccessService access, IOptions<AnnouncementSourceOptions> options,
    IOptions<AnnouncementOptions> module, TimeProvider time, ILogger<AnnouncementSourceService> logger)
{
    /// <summary>Draft fields a reviewed application is allowed to write.</summary>
    public static readonly string[] Applicable = ["Subject", "Scope", "Impact", "Checks", "Description", "AnnouncementDate", "OcoReference", "WorkStart", "WorkEnd"];

    /// <summary>Lists the protected profile allowlist with its configuration state.</summary>
    public Task<AnnouncementSourceOutcome> ProfilesAsync(ClaimsPrincipal principal, AccessOperationContext context, CancellationToken token) =>
        GuardedAsync(principal, context, async owner =>
        {
            await store.ReadAuditAsync(owner, null, null, "Profiles", context.CorrelationId, token);
            return new(Profiles: profiles.Choices());
        }, token);

    /// <summary>Records an explicitly owner-authorized job and enqueues it; a repeated key returns the existing job.</summary>
    public Task<AnnouncementSourceOutcome> SubmitAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid draftId, AnnouncementSourceSubmission submission, CancellationToken token) =>
        GuardedAsync(principal, context, async owner =>
        {
            if (!recovery.IsConfigured)
            { return new(Error: "AnnouncementSourceJobHostUnavailable"); }
            List<string> invalid = [];
            if (!MaintenanceProfiles.IsAllowed(submission.Profile))
            { invalid.Add("Profile"); }
            if (!IsOcoReference(submission.OcoReference))
            { invalid.Add("OcoReference"); }
            if (!IsSubmissionKey(submission.SubmissionKey))
            { invalid.Add("SubmissionKey"); }
            if (invalid.Count > 0)
            { return new(Error: "AnnouncementSourceInvalid", Fields: [.. invalid]); }
            MaintenanceProfileState state = profiles.Resolve(submission.Profile);
            if (state.State != "Configured")
            { return new(Error: "AnnouncementSourceProfileUnavailable", Fields: state.Missing); }
            if (await drafts.GetAsync(draftId, owner, token) is null)
            { return new(Error: "AnnouncementNotFound"); }
            DateTimeOffset now = time.GetUtcNow();
            var job = new AnnouncementSourceJob(Guid.NewGuid(), owner, draftId, submission.Profile,
                submission.OcoReference.Trim(), AnnouncementSourceJobStates.Queued, now, now, null, null);
            SqlAnnouncementSourceStore.SubmitResult accepted = await store.SubmitAsync(job, submission.SubmissionKey, context.CorrelationId, token);
            await recovery.DispatchAsync(accepted.Job.JobId, token);
            return new(Status: Status(accepted.Job, accepted.Duplicate ? accepted.Job.JobId : null));
        }, token);

    /// <summary>Owner-scoped durable status; poll only while the reported state is not terminal.</summary>
    public Task<AnnouncementSourceOutcome> StatusAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid draftId, Guid jobId, CancellationToken token) =>
        GuardedAsync(principal, context, async owner =>
        {
            AnnouncementSourceJob? job = jobId == Guid.Empty
                ? await store.LatestAsync(draftId, owner, token) : await store.GetAsync(jobId, owner, token);
            if (job is null || job.DraftId != draftId)
            { return new(Error: "AnnouncementSourceJobNotFound"); }
            await store.ReadAuditAsync(owner, draftId, job.JobId, "Status", context.CorrelationId, token);
            return new(Status: Status(job, null));
        }, token);

    /// <summary>Builds a reviewable difference between one snapshot and the live draft.</summary>
    public Task<AnnouncementSourceOutcome> ProposalAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid draftId, Guid jobId, CancellationToken token, string? reviewedSourceOffset = null) =>
        GuardedAsync(principal, context, async owner =>
        {
            (AnnouncementSourceOutcome? error, AnnouncementSourceJob? job, AnnouncementDraft? draft,
                MaintenanceProfileOptions? profile, AnnouncementSourceOverrides? overrides) = await LoadAsync(draftId, jobId, owner, token);
            if (error is not null)
            { return error; }
            await store.ReadAuditAsync(owner, draftId, jobId, "Proposal", context.CorrelationId, token);
            return new(Proposal: Build(job!, job!.Snapshot!, draft!, profile!, overrides!, reviewedSourceOffset));
        }, token);

    /// <summary>
    /// Applies a reviewed snapshot. The caller's expected version must match the current revision, a
    /// snapshot older than the applied one is refused, and unlisted fields keep the operator's text.
    /// Recipients are reconciled against manual additions and explicit removals, never replaced.
    /// </summary>
    public Task<AnnouncementSourceOutcome> ApplyAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid draftId, AnnouncementSourceApply request, CancellationToken token) =>
        GuardedAsync(principal, context, async owner =>
        {
            string[] requested = [.. (request.Fields ?? []).Distinct(StringComparer.Ordinal)];
            if (requested.Except(Applicable, StringComparer.Ordinal).Any())
            { return new(Error: "AnnouncementSourceInvalid", Fields: ["Fields"]); }
            (AnnouncementSourceOutcome? error, AnnouncementSourceJob? job, AnnouncementDraft? draft,
                MaintenanceProfileOptions? profile, AnnouncementSourceOverrides? overrides) = await LoadAsync(draftId, request.JobId, owner, token);
            if (error is not null)
            { return error; }
            if (draft!.Version != request.ExpectedVersion)
            { return new(Error: "AnnouncementConflict"); }
            if (overrides!.Version != request.ExpectedOverrideVersion)
            { return new(Error: "AnnouncementSourceOverrideConflict"); }
            if (job!.Snapshot!.ProfileFingerprint is not null
                && !string.Equals(request.ProfileFingerprint, job.Snapshot.ProfileFingerprint, StringComparison.Ordinal))
            { return new(Error: "AnnouncementSourceProfileChanged"); }
            if (overrides!.AppliedCapturedAt is { } applied && job!.Snapshot!.CapturedAt <= applied)
            {
                // A job that finished late must not overwrite content a newer snapshot already produced.
                logger.LogInformation("Refused a stale announcement source application. JobId: {JobId}.", job.JobId);
                return new(Error: "AnnouncementSourceStale");
            }
            AnnouncementSourceProposal proposal = Build(job!, job!.Snapshot!, draft, profile!, overrides, request.ReviewedSourceOffset);
            AnnouncementContent content = draft.Content;
            List<string> written = [], skipped = [];
            foreach (string field in requested)
            {
                ProposedField? candidate = proposal.Fields.FirstOrDefault(item => item.Field == field);
                if (candidate?.Proposed is null)
                { skipped.Add(field); continue; }
                content = Write(content, field, candidate.Proposed);
                written.Add(field);
            }
            bool services = request.ApplyAffectedServices
                && content.TemplateRevision is "oco-table-v2" or "oco-table-v3" && proposal.ProposedAffectedServices.Length > 0;
            if (services)
            { content = content with { AffectedServices = proposal.ProposedAffectedServices }; }
            else if (request.ApplyAffectedServices)
            { skipped.Add("AffectedServices"); }
            AnnouncementSourceOverrides next = overrides with { AppliedJobId = job.JobId, AppliedCapturedAt = job.Snapshot!.CapturedAt };
            if (request.ApplyRecipients)
            {
                MaintenanceProfileOptions? previous = overrides.Profile is null ? null : profiles.Configured(overrides.Profile);
                ReconciledRecipients to = RecipientReconciler.Reconcile(content.To, profile!.To, previous?.To ?? [], overrides.ManualTo, overrides.RemovedTo);
                ReconciledRecipients cc = RecipientReconciler.Reconcile(content.Cc, profile.Cc, previous?.Cc ?? [], overrides.ManualCc, overrides.RemovedCc);
                content = RecipientReconciler.WithRecipients(content, to.Difference.Proposed, cc.Difference.Proposed);
                next = next with { Profile = job.Profile, ManualTo = to.Manual, RemovedTo = to.Removed, ManualCc = cc.Manual, RemovedCc = cc.Removed };
            }
            AnnouncementOutcome save = await saves.ExecuteAsync(principal, context, draftId, draft.Version, "save", content, 1, 25,
                (revision, cancellation) => store.ApplyAsync(drafts, revision, next, request.ExpectedOverrideVersion,
                    [.. written], request.ApplyRecipients, context.CorrelationId, cancellation), token);
            if (save.Error is not null)
            { return new(Error: save.Error, Fields: save.Fields); }
            return new(Applied: new AnnouncementSourceApplyResult(draftId, save.Draft!.Version, [.. written],
                request.ApplyRecipients, services, [.. skipped.Distinct(StringComparer.Ordinal)]));
        }, token);

    // One owner-scoped load shared by proposal and apply; every miss is an explicit contract error.
    private async Task<(AnnouncementSourceOutcome? Error, AnnouncementSourceJob? Job, AnnouncementDraft? Draft,
        MaintenanceProfileOptions? Profile, AnnouncementSourceOverrides? Overrides)> LoadAsync(
        Guid draftId, Guid jobId, Guid owner, CancellationToken token)
    {
        AnnouncementSourceJob? job = await store.GetAsync(jobId, owner, token);
        if (job is null || job.DraftId != draftId)
        { return (new(Error: "AnnouncementSourceJobNotFound"), null, null, null, null); }
        if (job.Snapshot is null)
        { return (new(Error: job.ErrorCode ?? "AnnouncementSourceIncomplete"), null, null, null, null); }
        AnnouncementDraft? draft = await drafts.GetAsync(draftId, owner, token);
        if (draft is null)
        { return (new(Error: "AnnouncementNotFound"), null, null, null, null); }
        MaintenanceProfileOptions? profile = profiles.Configured(job.Profile);
        if (profile is not null && job.Snapshot.ProfileFingerprint is { } captured
            && !string.Equals(captured, MaintenanceProfileCatalog.Fingerprint(profile), StringComparison.Ordinal))
        { return (new(Error: "AnnouncementSourceProfileChanged"), null, null, null, null); }
        return profile is null
            ? (new(Error: "AnnouncementSourceProfileUnavailable", Fields: profiles.Resolve(job.Profile).Missing), null, null, null, null)
            : (null, job, draft, profile, await store.OverridesAsync(draftId, owner, token));
    }

    private AnnouncementSourceProposal Build(AnnouncementSourceJob job, AnnouncementSourceSnapshot snapshot,
        AnnouncementDraft draft, MaintenanceProfileOptions profile, AnnouncementSourceOverrides overrides, string? reviewedOffset = null)
    {
        AnnouncementContent content = draft.Content;
        string? start = SourceWindowEvidence.Resolve(snapshot.Work?.ProposedStartText, reviewedOffset);
        string? finish = SourceWindowEvidence.Resolve(snapshot.Work?.ProposedFinishText, reviewedOffset);
        if (snapshot.Work?.Resolution is "Invalid" or "Ambiguous" or "Failed" or "Missing")
        { start = null; finish = null; }
        if (start is not null && finish is not null && DateTimeOffset.Parse(finish, System.Globalization.CultureInfo.InvariantCulture)
            <= DateTimeOffset.Parse(start, System.Globalization.CultureInfo.InvariantCulture))
        { start = null; finish = null; }
        string? description = profile.DescriptionTemplate.Length == 0 ? profile.Description : start is null || finish is null ? null
            : profile.DescriptionTemplate.Replace("{WorkStart}", DisplayTime(start), StringComparison.Ordinal)
                .Replace("{WorkEnd}", DisplayTime(finish), StringComparison.Ordinal);
        ProposedField[] fields =
        [
            Text("Subject", content.Subject, $"{job.OcoReference} - Planlı Çalışma Duyurusu", "Profile"),
            Text("Scope", content.Scope, profile.Scope, "Profile"),
            Text("Impact", content.Impact, profile.Impact, "Profile"),
            Text("Checks", content.Checks, profile.Checks, "Profile"),
            Text("Description", content.Description, description, "Profile"),
            Text("OcoReference", content.OcoReference, job.OcoReference, "Submission"),
            // A source-local calendar date needs no timezone resolution; an instant would.
            Text("AnnouncementDate", content.AnnouncementDate, start?[..10] ?? snapshot.Work?.ProposedStartDate, "SourceStartDateProposal"),
            Window("WorkStart", content.WorkStart, snapshot.Work?.ProposedStartText, start, reviewedOffset, snapshot.Work?.Resolution),
            Window("WorkEnd", content.WorkEnd, snapshot.Work?.ProposedFinishText, finish, reviewedOffset, snapshot.Work?.Resolution),
            // Restart times are never inferred from an OCO finish time; the source does not state them.
            new("RestartStart", content.RestartStart, null, null, "NotDerivable", "SourceUnavailable"),
            new("RestartEnd", content.RestartEnd, null, null, "NotDerivable", "SourceUnavailable")
        ];
        MaintenanceProfileOptions? previous = overrides.Profile is null ? null : profiles.Configured(overrides.Profile);
        ReconciledRecipients to = RecipientReconciler.Reconcile(content.To, profile.To, previous?.To ?? [], overrides.ManualTo, overrides.RemovedTo);
        ReconciledRecipients cc = RecipientReconciler.Reconcile(content.Cc, profile.Cc, previous?.Cc ?? [], overrides.ManualCc, overrides.RemovedCc);
        SourceCompleteness done = snapshot.Completeness;
        return new(job.JobId, job.DraftId, job.Profile, job.State, snapshot.CapturedAt, draft.Version, fields,
            [.. snapshot.Services.Where(service => service.Resolution == "Resolved").Select(service => service.Name)],
            [.. snapshot.Services.Where(service => service.Resolution != "Resolved").Select(service => service.Name)],
            [.. snapshot.Devices.Select(device => device.Name)
                .Except(snapshot.Services.SelectMany(service => service.Devices), StringComparer.OrdinalIgnoreCase)],
            to.Difference, cc.Difference, profile.HighPriority, "DistributionRequest",
            new SourceCompletenessView(done.DevicesComplete, done.DeviceCount, done.DevicePagesRead, done.DevicesRequested,
                done.ServicesResolved, done.ServicesAmbiguous, done.ServicesMissing, done.ServicesFailed, done.Partial, done.Warnings),
            overrides.AppliedCapturedAt is { } applied && snapshot.CapturedAt <= applied, overrides.Version)
        { ProfileFingerprint = snapshot.ProfileFingerprint, ReviewedSourceOffset = reviewedOffset };
    }

    private static string DisplayTime(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
        .ToString("dd.MM.yyyy HH:mm:ss 'UTC' zzz", System.Globalization.CultureInfo.InvariantCulture);

    private static ProposedField Text(string field, string? current, string? proposed, string origin) =>
        new(field, current, string.IsNullOrWhiteSpace(proposed) ? null : proposed, null, origin,
            string.IsNullOrWhiteSpace(proposed) ? "SourceUnavailable"
                : string.Equals(current, proposed, StringComparison.Ordinal) ? "Unchanged" : "Changed");

    private static ProposedField Window(string field, string? current, string? sourceText, string? proposed, string? reviewedOffset, string? resolution) =>
        new(field, current, proposed, sourceText, SourceWindowEvidence.TryInstant(sourceText, out _) ? "SourceExplicitOffset"
            : reviewedOffset is null ? "SourceLocalText" : "OperatorReviewedOffset",
            sourceText is null ? "SourceUnavailable" : proposed is null
                ? resolution is "Unresolved" && reviewedOffset is null ? "RequiresOperatorOffset" : "Invalid"
                : proposed == current ? "Unchanged" : "Changed");

    private static AnnouncementContent Write(AnnouncementContent content, string field, string value) => field switch
    {
        "Subject" => content with { Subject = value },
        "WorkStart" => content with { WorkStart = value },
        "WorkEnd" => content with { WorkEnd = value },
        "Scope" => content with { Scope = value },
        "Impact" => content with { Impact = value },
        "Checks" => content with { Checks = value },
        "Description" => content with { Description = value },
        "AnnouncementDate" => content with { AnnouncementDate = value },
        "OcoReference" => content with { OcoReference = value },
        _ => content
    };

    private static AnnouncementSourceJobStatus Status(AnnouncementSourceJob job, Guid? duplicateOf) =>
        new(job.JobId, job.DraftId, job.Profile, job.OcoReference, job.State,
            AnnouncementSourceJobStates.IsTerminal(job.State), job.SubmittedAt, job.UpdatedAt, job.ErrorCode, duplicateOf);

    // Capabilities, approval and module opt-in are revalidated on every source request; nothing is cached.
    private async Task<AnnouncementSourceOutcome> GuardedAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Func<Guid, Task<AnnouncementSourceOutcome>> operation, CancellationToken token)
    {
        try
        {
            AccessServiceResult<EnsureAccessUserResult> current = await access.GetCurrentAsync(principal, context, token);
            if (!current.IsSuccess || current.Value!.User.Status != AccessStatus.Approved
                || !current.Value.User.Capabilities.Contains(Capabilities.AnnouncementDrafts))
            { return new(Error: "AccessDenied"); }
            if (!current.Value.User.Capabilities.Contains(Capabilities.AnnouncementSource))
            { return new(Error: "AnnouncementSourceAccessDenied"); }
            if (!module.Value.Enabled)
            { return new(Error: "AnnouncementsDisabled"); }
            if (!options.Value.Enabled)
            { return new(Error: "AnnouncementSourceDisabled"); }
            return await operation(current.Value.User.Id);
        }
        catch (AnnouncementSourceException exception)
        {
            logger.LogWarning("Announcement source request failed safely. ErrorCode: {ErrorCode}.", exception.ErrorCode);
            return new(Error: exception.ErrorCode);
        }
        catch (Exception exception) when (exception is System.Data.Common.DbException or InvalidOperationException or IOException)
        {
            logger.LogError("Announcement source request failed. FailureType: {FailureType}", exception.GetType().Name);
            return new(Error: "AnnouncementSourceUnavailable");
        }
    }

    private static bool IsOcoReference(string? value) => value is not null
        && Regex.IsMatch(value.Trim(), @"\A[A-Za-z0-9][A-Za-z0-9._/-]{0,63}\z");
    private static bool IsSubmissionKey(string? value) => value is not null
        && Regex.IsMatch(value, @"\A[A-Za-z0-9-]{8,128}\z");
}
