using MudBlazor;

namespace SecureOps.Ui.Shared;

/// <summary>
/// Canonical MudBlazor theme for the SecureOps operations shell, and the single source of truth for
/// colour, radius, and typography.
/// </summary>
/// <remarks>
/// <c>secureops-theme.css</c> defines no colours of its own: it maps the <c>--mud-palette-*</c>
/// variables this theme emits onto semantic <c>--so-*</c> tokens. That satisfies the single-source
/// requirement in <c>docs/20-ui-visual-design-contract.md</c> §7 and means a palette change here
/// reaches custom CSS and MudBlazor components together.
/// <para>
/// <b>Identity: corporate red, white, dark navy, neutral.</b> Red is the brand accent and, since the
/// corporate identity was adopted, also the primary action colour: selected navigation, primary
/// buttons, active tabs, and the flight route. Navy remains the structural colour of the app bar and
/// drawer, and the neutral greys carry content.
/// <para>
/// Because red now means "brand" as well as "problem", the two are separated on luminance rather
/// than hue — error sits markedly darker in Light and markedly lighter in Dark/Night than the brand
/// value — and no error state relies on colour at all: every one carries an icon, an explicit
/// message, and a semantic container. Red on its own never means failure.
/// </para>
/// </para>
/// <para>
/// <b>Dark palette.</b> Built as a ladder of elevation steps rather than one flat surface colour:
/// background → surface → raised surface each lighten slightly, so panels separate by luminance
/// instead of by borders. Pure black and pure white are avoided at both ends — the darkest ground is
/// a desaturated navy and the brightest text sits near #E6ECF5, which removes the halation that makes
/// long shifts on a dark screen tiring. Body and secondary text were chosen to clear WCAG AA (4.5:1)
/// against their own surface, and the accent is lightened relative to the light palette because a
/// mid-navy loses contrast against a dark ground.
/// </para>
/// </remarks>
public static class SecureOpsTheme
{
    /// <summary>
    /// Shared theme carrying both palettes. The active one is selected at runtime by
    /// <c>MudThemeProvider.IsDarkMode</c>, driven by a circuit-scoped toggle.
    /// </summary>
    // In MudBlazor 6.16 the light palette is MudTheme.Palette (obsolete in favor of the PaletteLight
    // *type*, which is assigned here). MudThemeProvider selects Palette or PaletteDark via IsDarkMode.
#pragma warning disable CS0618
    public static MudTheme Theme { get; } = new()
    {
        Palette = new PaletteLight
        {
            // Brand red is the primary action colour now that the corporate identity is
            // adopted. Error moves further from it in luminance so a failure never reads as a
            // button, and every error surface still carries an icon and explicit text.
            Primary = BrandRed,
            PrimaryDarken = BrandRedHover,
            PrimaryLighten = "#E8394E",
            Secondary = "#46566E",

            // Brand red. The Tertiary slot carries the identity colour so the single-source rule
            // holds: CSS reads it as --so-brand via --mud-palette-tertiary. No MudBlazor component
            // uses Color.Tertiary, so this slot has no other meaning. See the class remarks for why
            // red is confined to the chrome.
            Tertiary = BrandRed,

            Info = "#2E5A86",
            Success = "#0D7C66",
            Warning = "#B5731A",
            Error = "#8E1B16",
            Dark = "#14233F",
            Background = "#EEF1F6",
            BackgroundGrey = "#E4E9F1",
            Surface = "#FFFFFF",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#28344A",
            DrawerIcon = "#46566E",
            AppbarBackground = "#14233F",
            AppbarText = "#FFFFFF",
            TextPrimary = "#1A2433",
            TextSecondary = "#54607A",
            TextDisabled = "rgba(26,36,51,0.38)",
            ActionDefault = "#54607A",
            ActionDisabled = "rgba(26,36,51,0.26)",
            ActionDisabledBackground = "rgba(26,36,51,0.12)",
            Divider = "#DCE2EC",
            DividerLight = "#E8EDF4",
            LinesDefault = "#DCE2EC",
            LinesInputs = "#C2CBDA",
            TableLines = "#E2E7F0",
            TableHover = "rgba(31,61,102,0.04)"
        },
        PaletteDark = new PaletteDark
        {
            // Accent lightened from the light palette's #1F3D66: a mid navy reads as almost black
            // against a dark ground and fails contrast on filled buttons.
            Primary = BrandRedOnDark,
            PrimaryDarken = BrandRed,
            PrimaryLighten = "#FF6B7D",
            Secondary = "#8797AE",

            // Brand red, lifted from the light palette's #B81D2B: a deep crimson turns muddy and
            // loses its edge against a near-black navy ground.
            Tertiary = "#E05263",

            Info = "#6E9FD8",

            // Status colours desaturated and lightened so they stay distinguishable without glowing.
            Success = "#4FBFA2",
            Warning = "#E0B057",
            Error = "#FF8A80",

            Dark = "#080D17",

            // Elevation ladder. Each step is a small, even luminance increase, which is what gives the
            // dark theme depth; a single surface colour with borders reads flat and cheap.
            Background = "#0C1421",        // app ground
            BackgroundGrey = "#080D17",    // recessed areas
            Surface = "#141F31",           // panels
            DrawerBackground = "#101A2A",  // navigation, one step under panels
            AppbarBackground = "#0C1421",  // merges with the ground; separated by a hairline in CSS

            DrawerText = "#C9D4E4",
            DrawerIcon = "#93A3BC",
            AppbarText = "#E6ECF5",

            // ~13.5:1 on Surface. Deliberately short of pure white to reduce halation.
            TextPrimary = "#E6ECF5",
            // ~5.6:1 on Surface, clearing AA for the secondary text used in captions and helper rows.
            TextSecondary = "#A3B2C9",
            TextDisabled = "rgba(230,236,245,0.36)",

            ActionDefault = "#A3B2C9",
            ActionDisabled = "rgba(230,236,245,0.26)",
            ActionDisabledBackground = "rgba(230,236,245,0.10)",

            // Borders sit just above the surface they separate, so they read as edges rather than lines
            // drawn on top of the panel.
            Divider = "#243349",
            DividerLight = "#1B2739",
            LinesDefault = "#243349",
            LinesInputs = "#354866",
            TableLines = "#1F2C40",
            TableHover = "rgba(255,255,255,0.035)"
        },
        LayoutProperties = new LayoutProperties
        {
            DrawerWidthLeft = "248px",
            DefaultBorderRadius = "8px"
        },
        Typography = new Typography
        {
            Default = new Default
            {
                FontFamily = ["Segoe UI", "Segoe UI Variable", "system-ui", "Arial", "sans-serif"],
                LetterSpacing = "0"
            },
            H1 = new H1 { LetterSpacing = "0" },
            H2 = new H2 { LetterSpacing = "0" },
            H3 = new H3 { LetterSpacing = "0" },
            H4 = new H4 { LetterSpacing = "0" },
            H5 = new H5 { LetterSpacing = "0" },
            H6 = new H6 { LetterSpacing = "0" }
        }
    };

    /// <summary>
    /// The corporate red, sampled from the approved emblem rather than transcribed from a colour
    /// site.
    /// </summary>
    /// <remarks>
    /// <c>wwwroot/brand/approved/thy-emblem-master.png</c> is an indexed PNG whose palette is one
    /// red at 125 alpha steps. The fully opaque entry — and the dominant colour of the artwork — is
    /// this value, so it is the emblem's own red rather than an approximation of it.
    /// </remarks>
    public const string BrandRed = "#C90119";

    /// <summary>Hover/pressed step for the brand red.</summary>
    public const string BrandRedHover = "#A80115";

    /// <summary>Lighter brand red used where a dark ground would swallow the base value.</summary>
    public const string BrandRedOnDark = "#E8394E";

    /// <summary>
    /// Deep dark palette: the strongest expression of the corporate identity.
    /// </summary>
    /// <remarks>
    /// A near-black ground with a navy cast and cards that separate by luminance rather than by
    /// heavy borders. Error is deliberately pushed well away from the brand
    /// red in luminance: once red is also the primary action colour, a failure that merely "looks
    /// red" is indistinguishable from a button, so the two must differ on more than hue — and every
    /// error surface additionally carries an icon and explicit text.
    /// </remarks>
    private static readonly PaletteDark DeepDarkPalette = new()
    {
        Primary = BrandRedOnDark,
        PrimaryDarken = "#C90119",
        PrimaryLighten = "#FF6B7D",
        Secondary = "#8797AE",
        Tertiary = BrandRedOnDark,

        Info = "#6E9FD8",
        Success = "#4FBFA2",
        Warning = "#E0B057",
        Error = "#FF8A80",

        Background = "#05080F",        // app ground, near black with a navy cast
        BackgroundGrey = "#02040A",    // recessed areas
        Surface = "#0E141F",           // cards
        DrawerBackground = "#080D16",  // navigation, one step under cards
        AppbarBackground = "#05080F",
        AppbarText = "#E6ECF5",
        DrawerText = "#C3CEDF",
        DrawerIcon = "#8FA0B8",

        TextPrimary = "#EDF2F9",
        TextSecondary = "#9FAFC5",
        ActionDefault = "#9FAFC5",
        ActionDisabled = "rgba(237,242,249,0.30)",
        ActionDisabledBackground = "rgba(237,242,249,0.08)",

        Divider = "#1A2434",
        DividerLight = "#131B28",
        LinesDefault = "#1A2434",
        LinesInputs = "#2A3852",
        TableLines = "#161F2C",
        TableHover = "rgba(255,255,255,0.030)"
    };

    /// <summary>
    /// Returns the theme for an appearance mode.
    /// </summary>
    /// <param name="mode">Selected appearance.</param>
    /// <returns>The theme whose dark palette matches the mode.</returns>
    /// <remarks>
    /// MudBlazor carries one light and one dark palette per theme. Light and Dark keep the same
    /// light palette, so switching away from Dark and back does not change Light.
    /// </remarks>
    public static MudTheme For(AppearanceMode mode) => mode == AppearanceMode.Dark ? DeepDarkTheme : Theme;

    private static readonly MudTheme DeepDarkTheme = new()
    {
        Palette = Theme.Palette,
        PaletteDark = DeepDarkPalette,
        LayoutProperties = Theme.LayoutProperties,
        Typography = Theme.Typography
    };
#pragma warning restore CS0618
}

/// <summary>
/// The appearance modes an operator can choose.
/// </summary>
/// <remarks>
/// Presentation only. The choice is never sent to the API, never persisted server-side, and carries
/// no authorization meaning.
/// </remarks>
public enum AppearanceMode
{
    /// <summary>Light surfaces with charcoal text.</summary>
    Light,

    /// <summary>Near-black navy; the deep corporate presentation.</summary>
    Dark
}
