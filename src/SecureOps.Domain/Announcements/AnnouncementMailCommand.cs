using SecureOps.Domain.Commands;

namespace SecureOps.Domain.Announcements;

/// <summary>Frozen confirmed message identity and human authorization; contains no relay credentials.</summary>
public sealed record AnnouncementMailIntent(Guid CommandId, Guid PreparationId, Guid DraftId, long DraftVersion,
    string PreparationFingerprint, string Kind, OperationActor Initiator, long AccessVersion,
    string Sender, string EnvelopeSender, string[] To, string[] Cc, string Subject, string MessageId,
    string MessageHash, string ConfigurationFingerprint, string CorrelationId, string PreviewToken);

/// <summary>Durable command state; relay acceptance is not verified inbox delivery.</summary>
public sealed record AnnouncementMailCommand(AnnouncementMailIntent Intent, long Version, string State,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string[] Accepted, string[] Rejected, string? ErrorCode);

/// <summary>One fenced Worker execution carrying immutable bytes.</summary>
public sealed record AnnouncementMailExecution(AnnouncementMailCommand Command, Guid ExecutionToken, byte[] Message);
