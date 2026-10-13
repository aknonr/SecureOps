using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;
using NSubstitute;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.OperationalRecords;
using SecureOps.Ui.Pages;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>U1: ordering by an unknown source creation date is not offered as if the date were known.</summary>
public sealed class OperationalRecordListRenderTests
{
    [Fact]
    public async Task UnknownSourceDates_DisableOldestOrder_AndSayWhy()
    {
        string html = await RenderAsync(unknownDates: 2);

        html.Should().Contain("<option value=\"oldest\" disabled")
            .And.Contain("En eski kaynak tarihi (kullanılamıyor)")
            .And.Contain("aria-describedby=\"or-date-order-note\"")
            .And.Contain("2 kaydın kaynak oluşturma tarihi kaynaktan okunmadı");
    }

    [Fact]
    public async Task KnownSourceDates_KeepOldestOrderAvailable()
    {
        string html = await RenderAsync(unknownDates: 0);

        html.Should().Contain("<option value=\"oldest\">En eski kaynak tarihi</option>")
            .And.NotContain("data-date-order-note").And.NotContain("kullanılamıyor");
    }

    private static async Task<string> RenderAsync(int unknownDates)
    {
        AccessSnapshot snapshot = new(new(Guid.NewGuid(), "Approved", ["Admin"], AccessRoleCatalog.GetCapabilities(["Admin"]), null, "Demo",
            new(30, 12, true, true, "Lax", true, "Local")), null, DateTimeOffset.UtcNow);
        ICurrentAccessProvider access = Substitute.For<ICurrentAccessProvider>();
        access.GetAsync(Arg.Any<CancellationToken>()).Returns(snapshot);
        IOperationalRecordApiClient records = Substitute.For<IOperationalRecordApiClient>();
        OperationalRecordResponse record = JsonSerializer.Deserialize<OperationalRecordResponse>("""
            {"id":"00000000-0000-0000-0000-000000000123","sourceRecordId":"123","orCode":"OR-123",
             "title":"Synthetic","description":"Synthetic only","classification":0,"jiraEligible":true,
             "workflowState":3,"correlationId":"corr-123","version":1,"reasonCodes":[],"blockingConditions":[]}
            """, ApiResponseReader.JsonOptions)!;
        records.BrowseAsync(Arg.Any<OperationalRecordQuery>(), Arg.Any<CancellationToken>())
            .Returns(new OperationalRecordPageResponse([record], 1, 1, 25) { UnknownSourceCreatedAtCount = unknownDates });

        ServiceCollection registrations = new();
        registrations.AddLogging();
        registrations.AddMudServices();
        registrations.AddSingleton(access);
        registrations.AddSingleton(records);
        registrations.AddSingleton(Substitute.For<IJSRuntime>());
        registrations.AddSingleton<NavigationManager>(new StaticNavigation());
        await using ServiceProvider services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<SecureOps.Ui.Pages.OperationalRecords>()).ToHtmlString());
        return WebUtility.HtmlDecode(html);
    }

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation() => Initialize("http://localhost/", "http://localhost/operational-records");
    }
}
