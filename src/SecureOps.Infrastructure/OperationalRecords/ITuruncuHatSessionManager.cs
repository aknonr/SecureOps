namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Provides bounded, secret-safe Turuncu Hat sessions.</summary>
public interface ITuruncuHatSessionManager
{
    /// <summary>Returns a valid cached session or performs one single-flight login.</summary>
    public Task<string> GetSessionAsync(CancellationToken cancellationToken);

    /// <summary>Invalidates only the session that a dependency rejected.</summary>
    public void Invalidate(string rejectedSession);
}
