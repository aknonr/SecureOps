namespace SecureOps.Ui.Services;

/// <summary>Rejects responses issued before the current selection or access context.</summary>
public sealed class ResourceResolutionState<T> where T : class
{
    private long _generation;
    /// <summary>Only a response for the current context can be presented for opening.</summary>
    public T? Value { get; private set; }
    /// <summary>Invalidates displayed and pending results; also starts a new request.</summary>
    public long Clear() { Value = null; return ++_generation; }
    /// <summary>Checks whether a response or error still belongs to this context.</summary>
    public bool IsCurrent(long generation) => generation == _generation;
    /// <summary>Accepts an authoritative response only if its context has not changed.</summary>
    public bool Accept(long generation, T value)
    {
        if (!IsCurrent(generation))
        {
            return false;
        }
        Value = value;
        return true;
    }
}
