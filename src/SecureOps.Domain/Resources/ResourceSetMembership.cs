namespace SecureOps.Domain.Resources;

/// <summary>Non-destructive ordering of a partial personal projection. Inputs must be unique and disjoint.</summary>
public static class ResourceSetMembership
{
    /// <summary>Reuses requested members' slots, keeps omitted members in place, and appends surplus entries.</summary>
    public static Guid[] Merge(IReadOnlyList<Guid> saved, IReadOnlyList<Guid> ordered, IReadOnlyList<Guid> removed)
    {
        HashSet<Guid> selected = [.. ordered];
        HashSet<Guid> deleted = [.. removed];
        Queue<Guid> pending = new(ordered);
        List<Guid> result = [];
        foreach (Guid id in saved)
        {
            if (!deleted.Contains(id))
            {
                result.Add(selected.Contains(id) ? pending.Dequeue() : id);
            }
        }
        result.AddRange(pending);
        return [.. result];
    }
}
