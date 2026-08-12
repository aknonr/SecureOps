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
            Primary = "#1F3D66",
            PrimaryDarken = "#16294A",
            PrimaryLighten = "#33598C",
            Secondary = "#46566E",
            Tertiary = "#1F3D66",
            Info = "#2E5A86",
            Success = "#0D7C66",
            Warning = "#B5731A",
            Error = "#B3261E",
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
            Primary = "#6E9FD8",
            PrimaryDarken = "#4F7FB8",
            PrimaryLighten = "#94BCEA",
            Secondary = "#8797AE",
            Tertiary = "#94BCEA",
            Info = "#6E9FD8",

            // Status colours desaturated and lightened so they stay distinguishable without glowing.
            Success = "#4FBFA2",
            Warning = "#E0B057",
            Error = "#F08A80",

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
            DefaultBorderRadius = "10px"
        },
        Typography = new Typography
        {
            Default = new Default
            {
                FontFamily = ["Segoe UI", "Segoe UI Variable", "system-ui", "Arial", "sans-serif"],
                LetterSpacing = "0"
            },
            H1 = new H1 { LetterSpacing = "-0.01em" },
            H2 = new H2 { LetterSpacing = "-0.01em" },
            H3 = new H3 { LetterSpacing = "-0.01em" },
            H4 = new H4 { LetterSpacing = "-0.005em" },
            H5 = new H5 { LetterSpacing = "-0.005em" },
            H6 = new H6 { LetterSpacing = "0" }
        }
    };
#pragma warning restore CS0618
}
