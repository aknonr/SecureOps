using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Ui.Shared.Components.ServiceAccounts;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>The report view renders the activity page first, then at most five summary cards; empty groups say so. Synthetic data only.</summary>
public sealed class ServiceAccountActivityUiTests
{
    private static readonly DateTimeOffset _asOf = new(2026, 10, 16, 12, 0, 0, TimeSpan.FromHours(3));

    private static ServiceAccountReport Report(bool withWork)
    {
        var team = Guid.NewGuid();
        AccountFact account = new(Guid.NewGuid(), "SYN-svc-ui", team, null);
        ActionFact[] actions = withWork
            ? [new ActionFact(Guid.NewGuid(), account.Id, new ActionFacts(ServiceAccountActionType.Deletion, ServiceAccountActionResult.Performed,
                ServiceAccountRecordKind.Intermediate, new DateOnly(2026, 10, 14), null, false, true, false, false), TimePrecision.DateOnly, null, team)]
            : [];
        ReportFacts facts = new([account], [], actions, [], [], [], [], new Dictionary<Guid, string> { [team] = "SYN EKIP" }, Activity: new ActivityFacts([], [], [], false));
        return ServiceAccountMetrics.Compute(facts, new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 17), _asOf, "SYN", ReportPeriods.Custom);
    }

    [Fact]
    public async Task ActivityPage_RendersBeforeTheSummaryCards_WithHonestStatusAndNoPersonColumn()
    {
        string html = await RenderAsync(Report(true));

        html.IndexOf("Dönem içinde yapılan işlemler", StringComparison.Ordinal).Should().BeGreaterThan(-1)
            .And.BeLessThan(html.IndexOf("Yönetici özeti", StringComparison.Ordinal));
        html.Should().Contain("Silinen hesaplar").And.Contain("SYN-svc-ui").And.Contain("Bildirildi · doğrulanmadı")
            .And.Contain("“Bildirildi” doğrulandı anlamına gelmez").And.Contain("kişi bazlı sayı veya sıralama yoktur");
        html.Should().Contain("Bu dönemde kayıt yok", "empty groups are stated, not hidden");
        html.Split("class=\"sa-card\"").Length.Should().Be(6, "five cards");
        html.Should().Contain("Tüm göstergeler");
    }

    [Fact]
    public async Task EmptyPending_SaysNothingIsWaiting_AndPlansNoteIsShownWhenTablesAreMissing()
    {
        string html = await RenderAsync(Report(false));

        html.Should().Contain("Doğrulanmayı bekleyen işlem yok").And.Contain("plan tamamlanmaları sayılmadı");
    }

    private static async Task<string> RenderAsync(ServiceAccountReport report)
    {
        await using ServiceProvider services = new ServiceCollection().AddLogging().AddMudServices().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<SaReportView>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(SaReportView.Report)] = report, [nameof(SaReportView.Title)] = "Canlı rapor" }))).ToHtmlString()));
    }
}
