using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse.Execution;

/// <summary>Durable synthetic remote state for isolated process acceptance; never selected for corporate input.</summary>
public sealed class FixtureInUseCompletionTransport(IOptions<InUseCompletionOptions> options, InUseCompletionPolicy policy) : IInUseCompletionTransport
{
    /// <inheritdoc />
    public async Task<InUseRemoteResult> ExecuteAsync(string step, InUseExecutionLease execution, CancellationToken token)
    {
        if (options.Value.Provider != "Fixture" || !policy.Readiness(execution.Intent.Source).Available)
        { return new("Rejected", "FixtureBoundary"); }
        InUseSource source = execution.Intent.Source;
        string key = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { source.IdentityScope, source.Id })));
        Directory.CreateDirectory(options.Value.FixtureDirectory);
        string path = Path.Combine(options.Value.FixtureDirectory, key + ".json");
        await using FileStream file = new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096, true);
        FixtureState state = file.Length == 0 ? new(source.Id) : await JsonSerializer.DeserializeAsync<FixtureState>(file, cancellationToken: token)
            ?? throw new InvalidDataException("Invalid synthetic remote state.");
        if (state.SourceId != source.Id)
        { return new("Rejected", "TargetMismatch"); }
        string[] environments = source.Servers.Select(s => s.Fields.GetValueOrDefault("SI_ENVIRONMENT")?.Value?.ToUpperInvariant() ?? "").Distinct().ToArray();
        bool activity = execution.Intent.VerificationMode == "WasasActivityManual";
        string? targetEnvironment = activity ? InUseRequiredFields.Environment(source) : environments.Length == 1 ? environments[0] : null;
        InUseRemoteResult result;
        if (step == "Validate")
        {
            if (targetEnvironment is not ("PROD" or "DEV" or "TEST" or "NONPROD"))
            { return new("Rejected", "MixedOrUnknownEnvironment"); }
            if (state.ActivityCompleted)
            { return new("Rejected", "AlreadyActivityCompleted"); }
            if (state.Closed)
            { return new("Rejected", "AlreadyClosed"); }
            if (source.Servers.Any(s => string.IsNullOrWhiteSpace(s.Fields.GetValueOrDefault("ITMC_Service_ID")?.Value)
                || string.IsNullOrWhiteSpace(s.Fields.GetValueOrDefault("ITMC_Servis_Unsuru_ID")?.Value)))
            { return new("Rejected", "ServiceOrAspectMissing"); }
            return new("Verified", "SyntheticUniqueActivityAndSource", "fixture-task-" + key[..12]);
        }
        if (step == "Property4463")
        { state = state with { Category = "Application Server" }; result = new("Acknowledged", "SyntheticProperty4463"); }
        else if (step == "Property4464")
        {
            if (targetEnvironment is null)
            { return new("Rejected", "MixedEnvironment"); }
            state = state with { Environment = targetEnvironment };
            result = new("Acknowledged", "SyntheticProperty4464");
        }
        else if (step == "Upload")
        {
            string hash = Convert.ToHexString(SHA256.HashData(execution.Artifact));
            if (hash != execution.Intent.ReportSha256 || state.AttachmentId is not null)
            { return new("Rejected", "ArtifactOrDuplicateAttachment"); }
            state = state with { AttachmentId = "fixture-attachment-" + key[..12], Hash = hash, Content = execution.Artifact };
            result = new("Acknowledged", "SyntheticUploadAcknowledged", state.AttachmentId);
        }
        else if (step == "Attachment")
        {
            return state.Hash == execution.Intent.ReportSha256 && state.Content.SequenceEqual(execution.Artifact) && state.AttachmentId is not null
            ? new("Verified", "SyntheticExactAttachmentReadback", state.AttachmentId) : new("Unconfirmed", "AttachmentNotVerified");
        }
        else if (step == "Bpm")
        {
            if (!execution.Evidence.Any(e => e.Step == "Attachment" && e.Outcome == "Verified") || state.AttachmentId is null)
            { return new("Rejected", "AttachmentRequired"); }
            state = execution.Intent.VerificationMode == "WasasActivityManual"
                ? state with { ActivityCompleted = true } : state with { Closed = true };
            result = new("Acknowledged", "SyntheticActivityAcknowledged", "fixture-task-" + key[..12]);
        }
        else if (step == "Closure")
        { return state.Closed ? new("Verified", "SyntheticAuthoritativeOrClosed", source.Id) : new("Unconfirmed", "OrStillOpen"); }
        else
        { return new("Rejected", "UnsupportedStep"); }
        file.Position = 0;
        await JsonSerializer.SerializeAsync(file, state, cancellationToken: token);
        file.SetLength(file.Position);
        await file.FlushAsync(token);
        file.Flush(true);
        return result;
    }

    private sealed record FixtureState(string SourceId, string? Category = null, string? Environment = null,
        string? AttachmentId = null, string? Hash = null, bool Closed = false)
    {
        public byte[] Content { get; init; } = [];
        public bool ActivityCompleted { get; init; }
    }
}
