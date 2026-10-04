using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Ui.Shared.Components.ServiceAccounts;

namespace SecureOps.Tests.Unit.Ui;

public sealed class ServiceAccountChartUiTests
{
    private static async Task<string> RenderAsync(ReportChart chart)
    {
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        await using HtmlRenderer renderer = new(services, NullLoggerFactory.Instance);
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<SaChart>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Chart"] = chart }))).ToHtmlString());
        return Regex.Replace(html, " b-[a-z0-9]{10}", string.Empty); // scoped-CSS attributes
    }

    private static int Count(string html, string pattern) => Regex.Matches(html, pattern).Count;

    [Fact]
    public async Task LineChart_DrawsOneMarkerPerValue_WithShapesLegendAndATableOfTheSameValues()
    {
        ReportChart chart = new("trend", "Trend (son 3 hafta)", ReportChartKind.Line, ["01.09", "08.09", "15.09"],
            [new("Açık talep", [4, 6, 3]), new("Geciken", [1, 0, 2])], "Not");

        string html = await RenderAsync(chart);

        html.Should().Contain("<figure class=\"sa-chart\"").And.Contain("Trend (son 3 hafta)").And.Contain("<polyline");
        Count(html, "class=\"sa-mark sa-mark--1\"").Should().Be(3);
        Count(html, "class=\"sa-mark sa-mark--2\"").Should().Be(3);
        html.Should().Contain("sa-key--1").And.Contain("sa-key--2").And.Contain("son 3").And.Contain("son 2");
        html.Should().Contain("Tablo olarak göster").And.Contain("<th scope=\"row\">15.09</th><td>3</td><td>2</td>");
        html.Should().Contain("aria-hidden=\"true\"", "the drawing is decorative; the table carries the values for assistive technology");
    }

    [Fact]
    public async Task BarChart_PrintsEveryValue_AndHasNoLegendForASingleSeries()
    {
        ReportChart chart = new("gmsa", "gMSA geçişi", ReportChartKind.Bar, ["gMSA hedefli", "Geçişi gerçekleşen", "Bekleyen"],
            [new("Hesap", [10, 4, 6])]);

        string html = await RenderAsync(chart);

        html.Should().NotContain("sa-chart-legend");
        Count(html, "class=\"sa-hbar-value\"").Should().Be(3);
        html.Should().Contain("width:100%").And.Contain("width:40%").And.Contain("width:60%");
    }

    [Fact]
    public async Task EmptyChart_SaysSo_AndOpensTheTable()
    {
        ReportChart chart = new("teams", "Ekip iş yükü", ReportChartKind.Column, ["SYN TEAM"], [new("Sahip olduğu hesap", [0]), new("Geciken", [0])]);

        string html = await RenderAsync(chart);

        html.Should().Contain("tüm değerler 0").And.Contain("<details class=\"sa-chart-data\" open").And.NotContain("sa-hbar-fill");
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 4)]
    [InlineData(7, 8)]
    [InlineData(10, 10)]
    [InlineData(11, 20)]
    [InlineData(37, 40)]
    [InlineData(145, 200)]
    public void AxisTop_RoundsUpToAnEvenStep(long peak, long top) => SaChart.NiceTop(peak).Should().Be(top);

    [Fact]
    public void ReportView_ShowsTheChartsBeforeTheTables()
    {
        string view = File.ReadAllText(Path.Combine(Root(), "src", "SecureOps.Ui", "Shared", "Components", "ServiceAccounts", "SaReportView.razor"));

        view.IndexOf("<SaChart", StringComparison.Ordinal).Should().BeLessThan(view.IndexOf("sa-report-week", StringComparison.Ordinal));
        view.Should().Contain("ReportCharts.Build(Report)").And.NotContain("--sa-bar:");
    }

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SecureOps.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
