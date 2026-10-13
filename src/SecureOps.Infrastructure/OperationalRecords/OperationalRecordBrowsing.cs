using System.ComponentModel.DataAnnotations;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Shared.Contracts.OperationalRecords;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>A bounded repository page with counts from the same read snapshot.</summary>
/// <param name="Items">Requested page.</param>
/// <param name="Total">All matching records.</param>
/// <param name="UnknownSourceCreatedAtCount">Matching records whose source never supplied a creation time.</param>
public sealed record OperationalRecordPage(IReadOnlyList<OperationalRecord> Items, int Total, int UnknownSourceCreatedAtCount = 0);

/// <summary>Shared validation for SQL and local deterministic repositories.</summary>
internal static class OperationalRecordBrowsing
{
    internal static void Validate(OperationalRecordQuery query) =>
        Validator.ValidateObject(query, new ValidationContext(query), validateAllProperties: true);

    internal static OperationalRecordPage Query(IEnumerable<OperationalRecord> records, OperationalRecordQuery query, bool excludeSynthetic)
    {
        Validate(query);
        string search = query.Search?.Trim() ?? string.Empty;
        IEnumerable<OperationalRecord> matches = records.Where(r =>
            (!excludeSynthetic || (!SdmEvaluationEvidence.IsSynthetic(r.SourceRecordId) && !SdmEvaluationEvidence.IsSynthetic(r.OrCode)))
            && (query.State is null || r.WorkflowState == query.State)
            && (search.Length == 0 || r.OrCode.Contains(search, StringComparison.OrdinalIgnoreCase)
                || r.Title.Contains(search, StringComparison.OrdinalIgnoreCase)));
        IOrderedEnumerable<OperationalRecord> ordered = query.Sort switch
        {
            // An absent creation time is not an old one: unknown dates sort last.
            "oldest" => matches.OrderBy(r => r.CreatedAt is null).ThenBy(r => r.CreatedAt),
            "code" => matches.OrderBy(r => r.OrCode, StringComparer.OrdinalIgnoreCase),
            _ => matches.OrderByDescending(r => r.UpdatedAt)
        };
        OperationalRecord[] all = ordered.ThenBy(r => r.SourceRecordId, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(all.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray(), all.Length, all.Count(r => r.CreatedAt is null));
    }
}
