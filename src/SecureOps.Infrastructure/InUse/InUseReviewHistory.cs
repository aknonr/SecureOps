using System.Security.Cryptography;
using System.Text.Json;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

/// <summary>Stable identity and conservative source-context rules for explicit answer reuse.</summary>
public static class InUseReviewHistory
{
    /// <summary>Changes to any of these fields require individual review instead of inherited approval.</summary>
    public static IReadOnlyList<string> CriticalFields { get; } = ["SI_ENVIRONMENT", "NETWORK SEGMENT", "IP ADDRESS",
        "HOSTNAME", "ITMC_Service_ID", "OS NAME", "OS_VERSION", "ITMC_Servis_Unsuru_ID"];

    /// <summary>Null for legacy/unknown identities. Source object is fixed, never caller-selected.</summary>
    public static string? Identity(InUseSource source, InUseServer server) => string.IsNullOrWhiteSpace(source.IdentityScope) || string.IsNullOrWhiteSpace(server.Id)
        ? null : Hash(new { Scope = source.IdentityScope, Object = "LCSIMS_ServiceInstance", server.Id });

    /// <summary>Hash of exact source context, not display-normalized names or IP-based identity.</summary>
    public static string Context(InUseServer server) => Hash(CriticalFields.Select(f => new { Field = f, Value = server.Fields.GetValueOrDefault(f)?.Value }));

    /// <summary>Persisted observed labels/references only; never resolves a person from a display name.</summary>
    public static string SearchText(InUseServerReview review) => string.Join(" ", review.OrCode,
        review.Server.Fields.GetValueOrDefault("HOSTNAME")?.Value, review.Server.RelatedRequestReporter?.Display,
        review.Server.RelatedRequestReporter?.UserReference, review.ReviewerLabel);

    /// <summary>A new immutable snapshot for each explicit saved review and stable server identity.</summary>
    public static IReadOnlyList<InUseServerReview> Snapshots(InUseRecord record) => record.Draft is not { } draft ? []
        : record.Source.Servers.Where(s => Identity(record.Source, s) is not null).Select(s => new InUseServerReview(
            Guid.NewGuid(), record.Id, record.Source.Code, record.Version, record.SourceVersion, Identity(record.Source, s)!, Context(s), s,
            draft.Answers.Where(a => a.ServerId == s.Id).ToArray(), draft.ReviewedBy, draft.ReviewedByLabel,
            draft.ReviewedAt, record.LastSeenAt, draft.Policy)).ToArray();

    /// <summary>No automatic reuse, and no approval transfer across missing/stale/changed critical context.</summary>
    public static InUseReuseProposal Proposal(InUseRecord current, InUseServer server, InUseServerReview old, DateTimeOffset now)
    {
        string[] changed = CriticalFields.Where(f => server.Fields.GetValueOrDefault(f)?.Value != old.Server.Fields.GetValueOrDefault(f)?.Value).ToArray();
        string reason = old.Invalidated || current.Discarded ? "Taslak sıfırlanmış veya kaldırılmış; bu cevaplar yeniden kullanılamaz."
            : Identity(current.Source, server) != old.IdentityKey ? "Sunucu kaynak kimliği eşleşmiyor."
            : current.LastSeenAt > now || current.LastSeenAt < now.AddHours(-24) || old.ReviewedAt > now || old.ReviewedAt < now.AddDays(-30)
                || old.ObservedAt > old.ReviewedAt || old.ObservedAt < old.ReviewedAt.AddHours(-24)
                ? "Kaynak gözlemi veya önceki inceleme güncel değil; yeniden kontrol edin."
            : !InUseChecks.RelationshipReady(current.Source) ? "Kaynak ilişkisi doğrulanabilir durumda değil."
            : CriticalFields.Take(5).Any(f => string.IsNullOrWhiteSpace(server.Fields.GetValueOrDefault(f)?.Value))
                ? "Ortam, ağ, IP, sunucu adı veya servis kimliği eksik; yeniden kontrol edin."
            : changed.Length > 0 ? "Kritik kaynak alanları değişti; cevapları yeniden kontrol edin."
            : old.Answers.All(a => a.Origin?.AcceptedBy is null) ? "Önceki cevapların kabul kaydı yok; yeniden kontrol edin."
            : "Önceki cevaplar öneridir; seçtiklerinizi inceleyerek kabul edin.";
        return new(old.Id, reason.StartsWith("Önceki cevaplar öneridir", StringComparison.Ordinal), reason, changed);
    }

    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
