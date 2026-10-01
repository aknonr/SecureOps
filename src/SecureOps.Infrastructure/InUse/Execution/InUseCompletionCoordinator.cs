using Microsoft.Extensions.Logging;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse.Execution;

/// <summary>Prepares durable intent; never performs remote effects in the API request.</summary>
public sealed class InUseCompletionCoordinator(SqlInUseExecutionStore store, InUseReportArchive archive,
    InUseCompletionPolicy policy, InUseExecutionDispatcher dispatcher, InUsePolicy proposals, ILogger<InUseCompletionCoordinator> logger)
{
    /// <summary>Structured readiness and existing execution results.</summary>
    public async Task<InUseExecutionStatus> StatusAsync(InUseRecord record, bool authorized, CancellationToken token)
    {
        InUseExecutionReadiness readiness = policy.Readiness(record.Source);
        if (record.TrackingOnly)
        { readiness = new(false, "TrackingOnly", "Kaynakta tamamlanmış WASAS aktivitesi veya kapalı OR yalnız takip edilir. Yeni onay göndermeyin."); }
        else if (record.SourceObservationMissing)
        { readiness = new(false, "SourceNotObserved", "Kayıt son yenilemede görülmedi. Kaynak durumunu doğrulayın; cevaplar ve arşiv korunuyor."); }
        else if (record.HasActiveExecution)
        { readiness = new(false, "ExecutionPending", "Önceki işlemin sonucunu doğrulayın; yeni onay göndermeyin."); }
        if (readiness.Available && !authorized)
        { readiness = new(false, "AccessDenied", "Talebe ekleme ve tamamlama için ayrı yetki gerekir; inceleme izni yeterli değildir."); }
        if (readiness.Available && !dispatcher.Available)
        { readiness = new(false, "WorkerQueueUnavailable", "İş kuyruğu yapılandırılmamış; arşivleme kullanılabilir."); }
        return new(readiness, store.Configured ? await store.LatestAsync(record.Id, token) : null);
    }

    /// <summary>Archive and intent must exist before the recoverable enqueue attempt.</summary>
    public async Task<InUseResult<InUseExecution>> StartAsync(InUseRecord record, Guid actor, string label, StartInUseExecutionRequest request, CancellationToken token)
    {
        InUseExecutionReadiness readiness = policy.Readiness(record.Source);
        if (record.TrackingOnly || !readiness.Available || !dispatcher.Available)
        { return new(null, "InUseCompletionUnavailable", readiness.Explanation); }
        if (record.SourceObservationMissing || record.Version != request.ExpectedVersion || !InUseProgress.Ready(record)
            || record.Draft?.Policy?.Fingerprint != proposals.Propose(record).Fingerprint)
        { return new(null, "InUseConflict", "Kaynak, cevaplar veya ortam/NMS önerisi güncel değil. İnceleyip yeniden kaydedin."); }
        InUseReport? report = await archive.AccessAsync(record.Id, request.ExpectedVersion, null,
            r => Task.FromResult(r.SourceId == record.Source.Id && r.SourceVersion == record.SourceVersion && r.Sha256 == request.ReportSha256), token);
        if (report is null)
        { return new(null, "InUseConflict", "Bu sürümün değişmez WASAS arşivi bulunamadı; önce Excel'i kontrol edip arşivleyin."); }
        InUseExecutionIntent intent = new(request.CommandId, record.Id, report.Version, report.SourceVersion, record.SourceHash,
            report.Sha256, record.Source.Code + "_InUse.xlsx", actor, label, DateTimeOffset.UtcNow, policy.Fingerprint,
            SqlInUseExecutionStore.ReviewHash(record.Draft), record.Source)
        { VerificationMode = "WasasActivityManual" };
        InUseResult<InUseExecution> result = await store.CreateAsync(intent, report.Content, token);
        if (result.Error is null && result.Value is { State: "Queued" } operation)
        {
            try
            { dispatcher.Enqueue(operation.OperationId); }
            catch (Exception) { logger.LogWarning("In Use intent persisted; enqueue deferred to durable recovery. OperationId: {OperationId}", operation.OperationId); }
        }
        return result;
    }

    /// <summary>Local attestation remains available for reconciliation while remote writes are disabled.</summary>
    public Task<InUseResult<InUseExecution>> ConfirmClosureAsync(Guid recordId, Guid actor, string label,
        ConfirmInUseClosureRequest request, CancellationToken token) => store.ConfirmClosureAsync(recordId, actor, label, request, token);
}
