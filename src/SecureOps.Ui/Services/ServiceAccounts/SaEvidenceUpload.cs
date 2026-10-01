namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>An evidence file chosen in the browser, already bounded by the upload size limit.</summary>
/// <param name="OwnerType">Owning entity type (Account, Request, Action, Communication, Finding, Handover, Transition).</param>
/// <param name="OwnerId">Owning entity id.</param>
/// <param name="FileName">Original file name.</param>
/// <param name="ContentType">Browser-reported type (the server checks the signature).</param>
/// <param name="Content">Bytes.</param>
/// <param name="Label">Optional label.</param>
public sealed record SaEvidenceUpload(string OwnerType, Guid OwnerId, string FileName, string ContentType, byte[] Content, string? Label);
