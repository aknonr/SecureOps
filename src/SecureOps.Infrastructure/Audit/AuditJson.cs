using System.Text.Json;

namespace SecureOps.Infrastructure.Audit;

/// <summary>
/// Shared audit JSON settings.
/// </summary>
internal static class AuditJson
{
    /// <summary>
    /// JSON serializer options for audit records.
    /// </summary>
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
}
