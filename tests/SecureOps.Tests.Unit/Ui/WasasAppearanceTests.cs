using FluentAssertions;
using MudBlazor;
using SecureOps.Ui.Shared;


namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins the two appearance modes and the brand/error colour separation.
/// </summary>
/// <remarks>
/// Once corporate red became the primary action colour it also stopped being a reliable signal for
/// failure. These tests hold the line that keeps the two readable apart: they differ on luminance,
/// not only hue, and they differ in the direction that suits each ground.
/// </remarks>
public sealed class WasasAppearanceTests
{
    [Fact]
    public void BrandRed_ComesFromTheApprovedEmblem()
    {
        // The fully opaque palette entry of wwwroot/brand/approved/thy-emblem-master.png.
        SecureOpsTheme.BrandRed.Should().Be("#C90119");
    }

    [Fact]
    public void EmblemMaster_IsInTheRepositoryAndNotFetchedAtRuntime()
    {
        // The application must keep working if the original download is deleted.
        string brand = Path.Combine(UiRoot(), "wwwroot", "brand");

        File.Exists(Path.Combine(brand, "approved", "thy-emblem-master.png")).Should().BeTrue();
        File.Exists(Path.Combine(brand, "favicon-32.png")).Should().BeTrue();
        File.Exists(Path.Combine(brand, "favicon-16.png")).Should().BeTrue();
        File.Exists(Path.Combine(brand, "favicon.ico")).Should().BeTrue();
    }

    [Fact]
    public void FaviconAssets_StaySmallEnoughForATabIcon()
    {
        string brand = Path.Combine(UiRoot(), "wwwroot", "brand");

        new FileInfo(Path.Combine(brand, "favicon-32.png")).Length.Should().BeLessThan(8 * 1024);
        new FileInfo(Path.Combine(brand, "favicon.ico")).Length.Should().BeLessThan(16 * 1024);
    }

    [Fact]
    public void TwoModes_ResolveToATheme()
    {
        Enum.GetValues<AppearanceMode>().Should().HaveCount(2);

        foreach (AppearanceMode mode in Enum.GetValues<AppearanceMode>())
        {
            SecureOpsTheme.For(mode).Should().NotBeNull();
        }
    }

    [Fact]
    public void DarkUsesTheDeepCorporateGround()
    {
        Rgb(SecureOpsTheme.For(AppearanceMode.Dark).PaletteDark.Background.Value).Should().Be("05080f");
    }

    [Fact]
    public void LightAndDarkShareOneLightPalette()
    {
        // Switching to Dark and back must not change what Light looks like.
        SecureOpsTheme.For(AppearanceMode.Dark).PaletteLight.Primary.Value
            .Should().Be(SecureOpsTheme.For(AppearanceMode.Light).PaletteLight.Primary.Value);
    }

    [Fact]
    public void PrimaryAction_IsTheBrandRedInEveryMode()
    {
        // MudBlazor normalises to eight digits with an alpha pair, so compare the RGB prefix.
        Rgb(SecureOpsTheme.For(AppearanceMode.Light).PaletteLight.Primary.Value)
            .Should().Be(Rgb(SecureOpsTheme.BrandRed));

        foreach (AppearanceMode mode in new[] { AppearanceMode.Dark })
        {
            Rgb(SecureOpsTheme.For(mode).PaletteDark.Primary.Value)
                .Should().Be(Rgb(SecureOpsTheme.BrandRedOnDark));
        }
    }

    [Fact]
    public void ErrorStaysDistinguishableFromTheBrandRed()
    {
        // Separated on luminance, in the direction each ground needs: darker than brand on light,
        // lighter than brand on dark. Hue alone would not survive a glance.
        MudTheme light = SecureOpsTheme.For(AppearanceMode.Light);
        Luminance(light.PaletteLight.Error.Value)
            .Should().BeLessThan(Luminance(light.PaletteLight.Primary.Value));

        foreach (AppearanceMode mode in new[] { AppearanceMode.Dark })
        {
            Palette dark = SecureOpsTheme.For(mode).PaletteDark;
            Luminance(dark.Error.Value).Should().BeGreaterThan(Luminance(dark.Primary.Value));
        }
    }

    [Fact]
    public void AppearanceIsStoredWithoutACookieAndWithoutIdentity()
    {
        // A cookie would travel on every request and belongs to authentication. The preference is
        // presentation only, so it stays in local storage under a non-identifying key.
        string host = File.ReadAllText(Path.Combine(UiRoot(), "Pages", "_Host.cshtml"));
        string menu = File.ReadAllText(Path.Combine(UiRoot(), "Shared", "UserMenu.razor"));

        host.Should().Contain("wasas.appearance");
        host.Should().Contain("localStorage");
        host.Should().Contain("value === \"night\"");
        host.Should().Contain("localStorage.setItem(\"wasas.appearance\", \"dark\")");
        host.Should().NotContain("document.cookie");
        menu.Should().Contain("Aydınlık");
        menu.Should().Contain("Koyu");
        menu.Should().NotContain("Gece");
        menu.Should().NotContain("AppearanceMode.Night");
    }

    private static string Rgb(string hex) => hex.TrimStart('#')[..6].ToLowerInvariant();

    private static int Luminance(string hex)
    {
        string value = hex.TrimStart('#');
        int r = Convert.ToInt32(value.Substring(0, 2), 16);
        int g = Convert.ToInt32(value.Substring(2, 2), 16);
        int b = Convert.ToInt32(value.Substring(4, 2), 16);

        // Rec. 601 luma: close enough to perceived brightness for an ordering assertion.
        return (299 * r + 587 * g + 114 * b) / 1000;
    }

    private static string UiRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "src", "SecureOps.Ui");
    }
}

