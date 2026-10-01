namespace SecureOps.Ui.Services;

/// <summary>
/// The product name as operators see it.
/// </summary>
/// <remarks>
/// The visible product is WASAS Otomasyon Yönetimi. SecureOps remains the technical identity and is
/// deliberately untouched: namespaces, assemblies, API routes, configuration prefixes, cookie names,
/// and the Data Protection application names all still say SecureOps, because renaming any of them
/// would be a breaking infrastructure change wearing the costume of a branding task.
/// <para>
/// Kept as constants rather than scattered literals so the visible name has one definition. Support
/// surfaces may still show the technical identifier where it helps somebody correlate a log line.
/// </para>
/// </remarks>
public static class WasasBrand
{
    /// <summary>Full product name for titles, headers, and the sign-in card.</summary>
    public const string Name = "WASAS Otomasyon Yönetimi";

    /// <summary>Short form for constrained space such as a collapsed app bar.</summary>
    public const string ShortName = "WASAS";

    /// <summary>Second line when the name is stacked over two lines.</summary>
    public const string NameSuffix = "Otomasyon Yönetimi";

    /// <summary>One-line description of what the application is for.</summary>
    public const string Tagline = "Kurumsal operasyon ve erişim yönetim platformu";

    /// <summary>
    /// Builds a browser tab title.
    /// </summary>
    /// <param name="page">Page name, or <see langword="null"/> for the product name alone.</param>
    /// <returns>The document title.</returns>
    /// <remarks>
    /// Page name first: a row of pinned internal tools is told apart by what each tab is showing,
    /// not by the product name repeated at the front of every one of them.
    /// </remarks>
    public static string Title(string? page = null) =>
        string.IsNullOrWhiteSpace(page) ? Name : $"{page} · {Name}";
}
