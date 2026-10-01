using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;

namespace SecureOps.Tests.Unit.Audit;

public sealed class BuildToolchainContractTests
{
    [Fact]
    public void Toolchain_IsExplicitWithoutChangingProductRuntime()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }
        root.Should().NotBeNull();
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName, "global.json")));
        JsonElement sdk = json.RootElement.GetProperty("sdk");
        sdk.GetProperty("version").GetString().Should().Be("9.0.317");
        sdk.GetProperty("rollForward").GetString().Should().Be("disable");
        sdk.GetProperty("allowPrerelease").GetBoolean().Should().BeFalse();
        XElement props = XElement.Load(Path.Combine(root.FullName, "Directory.Build.props"));
        props.Descendants("TargetFramework").Single().Value.Should().Be("net8.0");
        props.Descendants("LangVersion").Single().Value.Should().Be("12.0");
        props.Descendants("AnalysisLevel").Single().Value.Should().Be("9.0");
    }
}
