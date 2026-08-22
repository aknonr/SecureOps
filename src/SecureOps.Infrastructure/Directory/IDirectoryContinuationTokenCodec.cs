namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Protects opaque, operation-bound Directory Explorer continuation tokens.</summary>
public interface IDirectoryContinuationTokenCodec
{
    /// <summary>Creates a target-bound continuation token.</summary>
    public string Create(string operation, string normalizedTarget, int offset);

    /// <summary>Validates a target-bound continuation token and returns its offset.</summary>
    public bool TryRead(string? token, string operation, string normalizedTarget, out int offset);
}
