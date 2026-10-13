namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>One usage-scan file chosen in the browser for several selected accounts (ADR-0027); the API checks every account.</summary>
/// <param name="FileName">Original file name.</param>
/// <param name="Content">Bytes, already bounded by the upload size limit.</param>
/// <param name="RunStatement">Where and under which authority the person ran the scan.</param>
/// <param name="AccountIds">Selected accounts (1–20, distinct).</param>
public sealed record SaUsageScanBatchUpload(string FileName, byte[] Content, string RunStatement, IReadOnlyList<Guid> AccountIds);
