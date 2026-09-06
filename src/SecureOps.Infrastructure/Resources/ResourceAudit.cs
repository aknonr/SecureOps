using SecureOps.Infrastructure.Audit;

namespace SecureOps.Infrastructure.Resources;

/// <summary>Shared safe audit envelope used by both persistence providers.</summary>
internal static class ResourceAudit
{
    internal static AuditEvent Change(string kind, Guid id, long version, ResourceActor actor, DateTimeOffset now,
        bool? archived = null, bool? active = null) => new()
        {
            Actor = actor.UserId.ToString("D"),
            Action = "Resource" + kind + "Saved",
            CorrelationId = actor.CorrelationId,
            OccurredAt = now,
            Details = new { EntryId = id, Version = version, Change = version == 1 ? "Created" : "Replaced", Archived = archived, Active = active }
        };
}
