using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;
using NSubstitute;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;
using SecureOps.Ui.Services.ServiceAccounts;
using SecureOps.Ui.Shared.Components.ServiceAccounts;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// The multi-account scan form on the account list (ADR-0027) rendered with synthetic data: it says what the server checks,
/// bounds the selection, and shows the server's per-account answer with a text label for every outcome, never the name of
/// an account the server did not return.
/// </summary>
public sealed class ServiceAccountUsageScanBatchUiTests
{
    private static readonly Guid _a = Guid.Parse("5a1e0000-0000-4000-8000-0000000000a1");
    private static readonly Guid _b = Guid.Parse("5a1e0000-0000-4000-8000-0000000000b2");

    [Fact]
    public async Task BeforeUpload_ExplainsTheChecks_AndListsTheSelection()
    {
        string html = Words(await RenderAsync([_a, _b], ["svc_syn_a", "svc_syn_b"], null));

        html.Should().Contain("Tek tarama dosyasını seçili hesaplara bağla").And.Contain("Seçili hesaplar (2): svc_syn_a, svc_syn_b");
        html.Should().Contain("Bir hesabın reddi diğerlerini engellemez").And.Contain("hesabın kendi sayfasından yükleyin")
            .And.Contain("dosyanın tamamı reddedilir");
        html.Should().Contain("Çalıştırma beyanı (zorunlu)").And.Contain("Yükle ve seçili hesaplara bağla");
        html.Should().NotContain("Son yüklemenin sonucu");
    }

    [Fact]
    public async Task MoreThanTwentySelected_WarnsAndCannotSend()
    {
        Guid[] ids = [.. Enumerable.Range(1, 21).Select(n => Guid.Parse($"5a1e0000-0000-4000-8000-{n:000000000000}"))];

        string html = await RenderAsync(ids, [.. ids.Select((_, n) => $"svc_syn_{n}")], null);

        Words(html).Should().Contain("en çok 20 hesap seçilebilir; 21 hesap seçili");
        Regex.IsMatch(html, @"<button[^>]*disabled[^>]*>(?:(?!</button>).)*Yükle ve seçili hesaplara bağla", RegexOptions.Singleline).Should().BeTrue();
    }

    [Fact]
    public async Task Result_ShowsEveryOutcomeWithText_AndNoNameTheServerWithheld()
    {
        UsageScanBatchResult result = new(Guid.NewGuid(), "Discovery", 4, 3,
        [
            Row(_a, "svc_syn_a", UsageScanBatchOutcome.Attached, "SYN\\svc_syn_a"),
            Row(_b, "svc_syn_b", UsageScanBatchOutcome.AlreadyAttached, "SYN\\svc_syn_b"),
            Row(Guid.NewGuid(), "svc_syn_c", UsageScanBatchOutcome.NotInScan, null),
            Row(Guid.NewGuid(), "svc_syn_d", UsageScanBatchOutcome.Ambiguous, null),
            Row(Guid.NewGuid(), null, UsageScanBatchOutcome.Unavailable, null),
            Row(Guid.NewGuid(), "svc_syn_f", UsageScanBatchOutcome.Failed, null)
        ]);

        string html = Words(await RenderAsync([_a, _b], ["svc_syn_a", "svc_syn_b"], result));

        html.Should().Contain("6 hesaptan 1 tanesine bağlandı, 1 tanesi zaten bağlıydı, 3 tanesine bağlanmadı, 1 tanesi kaydedilemedi")
            .And.Contain("Kullanım taraması, 4 planlanan sunucudan 3 tanesinden sonuç var");
        foreach (string label in new[] { "Bağlandı", "Zaten bağlıydı", "Dosyada yok", "Belirsiz", "Bulunamadı / yetki yok", "Kaydedilemedi" })
        {
            html.Should().Contain(label);
        }

        html.Should().Contain("Bulunamadı veya kapsamınızda değil").And.Contain("Dosyadaki ad: SYN\\svc_syn_a");
        html.Should().Contain(UsageScanBatch.Label(UsageScanBatchOutcome.Ambiguous), "the row carries the server's own wording");
        html.Should().NotContain("kullanılmıyor");
    }

    private static UsageScanBatchAccountResult Row(Guid id, string? name, UsageScanBatchOutcome outcome, string? matched) =>
        new(id, name, name is null ? null : "SYN", outcome.ToString(), UsageScanBatch.Label(outcome), matched);

    private static async Task<string> RenderAsync(IReadOnlyList<Guid> ids, IReadOnlyList<string> labels, UsageScanBatchResult? result)
    {
        ServiceCollection registrations = new();
        registrations.AddLogging();
        registrations.AddMudServices();
        registrations.AddSingleton(Substitute.For<IJSRuntime>());
        registrations.AddSingleton<NavigationManager>(new SyntheticNavigation());
        await using ServiceProvider services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<SaUsageScanBatchForm>(ParameterView.FromDictionary(
            new Dictionary<string, object?> { ["AccountIds"] = ids, ["AccountLabels"] = labels, ["Busy"] = false, ["Result"] = result }))).ToHtmlString());
        return WebUtility.HtmlDecode(Regex.Replace(html, " b-[a-z0-9]{10}", string.Empty));
    }

    private sealed class SyntheticNavigation : NavigationManager
    {
        public SyntheticNavigation() => Initialize("http://localhost/", "http://localhost/service-accounts");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Unexpected navigation.");
    }

    private static string Words(string html) => Regex.Replace(Regex.Replace(html, "<[^>]+>", " "), @"\s+", " ");
}
