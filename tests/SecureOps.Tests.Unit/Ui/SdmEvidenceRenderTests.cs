using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Shared.Contracts.OperationalRecords;
using SecureOps.Ui.Services;
using SecureOps.Ui.Shared.Components;

namespace SecureOps.Tests.Unit.Ui;

public sealed class SdmEvidenceRenderTests
{
    [Fact]
    public async Task JiraOnlySteps_DoNotAnnounceAnOutstandingSourceClose()
    {
        await using ServiceProvider services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent rendered = await renderer.RenderComponentAsync<SoOperatorSteps>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(SoOperatorSteps.Current)] = 4,
                [nameof(SoOperatorSteps.IncludeSourceClose)] = false
            }));
            return rendered.ToHtmlString();
        });
        System.Net.WebUtility.HtmlDecode(html).Should().NotContain("Kaynak Kaydı Tamamla").And.NotContain("sıradaki adım");
    }

    [Fact]
    public async Task ReportingWindow_ExposesSelectedTabAsAnAriaBooleanString()
    {
        await using ServiceProvider services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent rendered = await renderer.RenderComponentAsync<SoWindowPicker>();
            return rendered.ToHtmlString();
        });
        html.Should().Contain("aria-selected=\"true\"").And.Contain("aria-selected=\"false\"");
    }

    [Fact]
    public async Task PersistedEvidence_RendersActionableBlockersWithoutGrantingApproval()
    {
        OperationalRecordResponse record = JsonSerializer.Deserialize<OperationalRecordResponse>("""
            {"id":"00000000-0000-0000-0000-000000000123","sourceRecordId":"123","orCode":"OR-123",
             "title":"Synthetic local evidence","description":"Synthetic only","jiraEligible":false,
             "reasonCodes":[],"blockingConditions":[]}
            """, ApiResponseReader.JsonOptions)!;
        SdmEvaluationResult evaluation = SdmEvaluator.Evaluate(new(new string('a', 64), ProviderSupported: true,
            Active: true, ValidId: true, ValidCode: true, ValidTitle: true, ValidDescription: true));
        record = record with
        {
            EvaluatedAt = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero),
            RuleSetVersion = evaluation.RuleSetVersion,
            ReasonCodes = evaluation.ReasonCodes,
            BlockingConditions = evaluation.BlockingConditions
        };
        await using ServiceProvider services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent rendered = await renderer.RenderComponentAsync<SoSdmEvidence>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(SoSdmEvidence.Record)] = record }));
            return rendered.ToHtmlString();
        });
        string decoded = System.Net.WebUtility.HtmlDecode(html);
        decoded.Should().Contain("Sunucu Talebi ve Uygulama Kurulumu kapsamda").And.Contain("Talep türü seçimi yayımlama onayı yerine geçmez");
        decoded.Should().Contain("Sunucu veya IP kanıtı yok").And.NotContain("<button");
        SdmEvidenceView.SameSource(record, record with { Version = 9 }).Should().BeTrue();
        SdmEvidenceView.SameSource(record, record with { Description = "Changed" }).Should().BeFalse();
        SdmEvidenceView.SameSource(record, record with { SourceChanged = true }).Should().BeFalse();
        string? evidenceDirectory = Environment.GetEnvironmentVariable("SECUREOPS_UI_EVIDENCE_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            Directory.CreateDirectory(evidenceDirectory);
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "synthetic-sdm-component.html"),
                "<!doctype html><html lang=\"tr\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
                + "<link rel=\"stylesheet\" href=\"https://localhost:6497/_content/MudBlazor/MudBlazor.min.css\">"
                + "<link rel=\"stylesheet\" href=\"https://localhost:6497/css/secureops-theme.css\"><body><main class=\"so-page\">"
                + "<h1>Yerel sentetik SDM bileşen doğrulaması</h1>" + html + "</main></body></html>");
        }
    }
}
