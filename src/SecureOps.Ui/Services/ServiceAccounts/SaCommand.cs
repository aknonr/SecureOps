namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>
/// A detail-page command raised by a section component. The page sends it, replaces the account view with the
/// server's answer and bumps its revision so sections clear their forms only after a confirmed save.
/// </summary>
/// <param name="Method">HTTP method.</param>
/// <param name="Path">Module-relative path.</param>
/// <param name="Body">JSON body.</param>
/// <param name="ReturnsDetail">True when the endpoint answers with the updated account detail.</param>
public sealed record SaCommand(HttpMethod Method, string Path, object Body, bool ReturnsDetail = true);
