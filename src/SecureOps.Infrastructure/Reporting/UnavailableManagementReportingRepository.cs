namespace SecureOps.Infrastructure.Reporting;

/// <summary>Fail-closed repository used when all authoritative SQL providers are not selected.</summary>
public sealed class UnavailableManagementReportingRepository : IManagementReportingRepository
{
    /// <inheritdoc />
    public Task<ManagementReportingData> GetSummaryAsync(ReportingWindow window, CancellationToken cancellationToken) =>
        Task.FromException<ManagementReportingData>(Unavailable());

    /// <inheritdoc />
    public Task<OperatorActivityDataPage> GetOperatorActivityAsync(
        ReportingWindow window,
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        Task.FromException<OperatorActivityDataPage>(Unavailable());

    private static ReportingUnavailableException Unavailable() =>
        new("Management reporting requires SQL Audit, Access, and Operational Record persistence.");
}
