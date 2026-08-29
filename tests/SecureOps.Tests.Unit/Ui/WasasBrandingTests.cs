using System.Text.RegularExpressions;
using FluentAssertions;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins the visible product identity.
/// </summary>
/// <remarks>
/// The rename is user-facing only. SecureOps remains the technical identity everywhere it is load
/// bearing — namespaces, assemblies, routes, configuration prefixes, cookie names — so these tests
/// check what an operator reads, not what the code is called.
/// </remarks>
public sealed class WasasBrandingTests
{
    [Fact]
    public void ProductName_IsWasas()
    {
        WasasBrand.Name.Should().Be("WASAS Otomasyon Yönetimi");
        WasasBrand.ShortName.Should().Be("WASAS");
    }

    [Fact]
    public void DocumentTitle_LeadsWithThePageThenTheProduct()
    {
        // A row of pinned internal tools is told apart by what each tab shows, not by the product
        // name repeated at the front of every one of them.
        WasasBrand.Title("Aktif Oturumlar").Should().Be("Aktif Oturumlar · WASAS Otomasyon Yönetimi");
    }

    [Fact]
    public void DocumentTitle_FallsBackToTheProductNameAlone()
    {
        WasasBrand.Title(null).Should().Be(WasasBrand.Name);
        WasasBrand.Title("   ").Should().Be(WasasBrand.Name);
    }

    [Fact]
    public void EveryPageTitle_CarriesTheProductName()
    {
        foreach (string file in RazorPages())
        {
            string content = File.ReadAllText(file);
            Match title = Regex.Match(content, @"<PageTitle>(?<value>.*?)</PageTitle>", RegexOptions.Singleline);

            if (!title.Success)
            {
                continue;
            }

            title.Groups["value"].Value.Should().Contain(
                "WasasBrand",
                "the page title in {0} must come from the single brand definition",
                Path.GetFileName(file));
        }
    }

    [Fact]
    public void NoVisibleTextStillSaysSecureOps()
    {
        // Comments, @using directives, and capability identifiers legitimately keep the technical
        // name. Rendered copy must not.
        List<string> offenders = [];

        foreach (string file in RazorPages().Concat(RazorComponents()).Concat(RazorPagesCshtml()))
        {
            // Razor block comments wrap freely, so their continuation lines carry no marker of their
            // own. Tracking the open/close pair is the only way to tell prose inside a comment from
            // prose that will actually be rendered.
            bool insideBlockComment = false;

            foreach (string line in File.ReadLines(file))
            {
                string trimmed = line.TrimStart();
                bool opensBlock = trimmed.Contains("@*", StringComparison.Ordinal);
                bool closesBlock = trimmed.Contains("*@", StringComparison.Ordinal);

                bool isComment = insideBlockComment
                    || opensBlock
                    || trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("///", StringComparison.Ordinal);

                if (opensBlock && !closesBlock)
                {
                    insideBlockComment = true;
                }
                else if (closesBlock)
                {
                    insideBlockComment = false;
                }

                bool isCode = trimmed.StartsWith("@using", StringComparison.Ordinal)
                    || trimmed.StartsWith("@namespace", StringComparison.Ordinal)
                    || trimmed.StartsWith("@inject", StringComparison.Ordinal)
                    || trimmed.StartsWith("@model", StringComparison.Ordinal)
                    || trimmed.Contains("SecureOpsTheme", StringComparison.Ordinal)
                    || trimmed.Contains("SecureOpsApiException", StringComparison.Ordinal)
                    || trimmed.Contains("ILogger<", StringComparison.Ordinal);

                if (!isComment && !isCode && line.Contains("SecureOps", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {trimmed}");
                }
            }
        }

        offenders.Should().BeEmpty("visible product branding must read WASAS");
    }

    [Fact]
    public void LoginCta_DoesNotClaimCorporateSsoWhileOnlyTestSignInExists()
    {
        // The interim path establishes a local TEST session. Labelling it corporate SSO would tell
        // an operator their corporate credentials had been verified when nothing checked them.
        string login = File.ReadAllText(Path.Combine(UiRoot(), "Pages", "Login.cshtml"));

        login.Should().Contain("TEST Girişi");
        login.Should().NotContain("Kurumsal SSO ile Giriş");
    }

    [Fact]
    public void FaviconAndTitle_AreWiredFromLocalAssets()
    {
        // No remote origin may serve application identity: a logo fetched at runtime is an external
        // dependency on a page that must work on an isolated corporate network.
        string host = File.ReadAllText(Path.Combine(UiRoot(), "Pages", "_Host.cshtml"));

        host.Should().Contain("@WasasBrand.Name");
        host.Should().Contain("brand/favicon-32.png");
        host.Should().Contain("brand/favicon-16.png");
        host.Should().Contain("brand/favicon.ico");
        host.Should().NotContain("http://");
        host.Should().NotContain("seeklogo");
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

    private static IEnumerable<string> RazorPages() =>
        Directory.EnumerateFiles(Path.Combine(UiRoot(), "Pages"), "*.razor");

    private static IEnumerable<string> RazorPagesCshtml() =>
        Directory.EnumerateFiles(Path.Combine(UiRoot(), "Pages"), "*.cshtml");

    private static IEnumerable<string> RazorComponents() =>
        Directory.EnumerateFiles(Path.Combine(UiRoot(), "Shared"), "*.razor", SearchOption.AllDirectories);
}
