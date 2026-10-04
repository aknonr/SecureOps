namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>A usage-scan file chosen in the browser (ADR-0027), already bounded by the upload size limit; the API decides.</summary>
/// <param name="FileName">Original file name.</param>
/// <param name="Content">Bytes.</param>
/// <param name="RunStatement">Where and under which authority the person ran the scan.</param>
/// <param name="RequestId">A participant's own open request; null for the responsible team.</param>
public sealed record SaUsageScanUpload(string FileName, byte[] Content, string RunStatement, Guid? RequestId);
