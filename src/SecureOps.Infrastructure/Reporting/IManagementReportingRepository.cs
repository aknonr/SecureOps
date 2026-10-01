namespace SecureOps.Infrastructure.Reporting;

/// <summary>Persistent backend reporting aggregation boundary.</summary>
public interface IManagementReportingRepository
{
    /// <summary>Returns a server-aggregated management summary data set.</summary>
    public Task<ManagementReportingData> GetSummaryAsync(ReportingWindow window, CancellationToken cancellationToken);

    /// <summary>Returns one server-aggregated, paginated operator activity page.</summary>
    public Task<OperatorActivityDataPage> GetOperatorActivityAsync(
        ReportingWindow window,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}

/// <summary>Signals that authoritative persistent reporting is not available.</summary>
public class ReportingUnavailableException : Exception
{
    /// <summary>Initializes the exception.</summary>
    public ReportingUnavailableException(string message)
        : base(message)
    {
    }
}

/// <summary>Raised when the authoritative reporting persistence set is intentionally not configured.</summary>
public sealed class ReportingPersistenceNotConfiguredException : ReportingUnavailableException
{
    /// <summary>Initializes the exception.</summary>
    public ReportingPersistenceNotConfiguredException(string message)
        : base(message)
    {
    }
}
