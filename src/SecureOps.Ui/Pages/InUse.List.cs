using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.InUse;
using SecureOps.Ui.Services;
using SecureOps.Ui.Shared.Components;

namespace SecureOps.Ui.Pages;

public partial class InUse
{
    private IReadOnlyList<InUseCount> _counts = [];
    private bool _recount;

    /// <summary>Server totals for the status strip. A total that cannot be read stays unknown, never zero.</summary>
    private async Task ReadCountsAsync()
    {
        async Task<int?> TotalAsync(string view)
        {
            try
            { return (await ReadAsync<InUsePage>($"?view={view}&page=1&pageSize=10")).Total; }
            catch (SecureOpsApiException)
            { return null; }
        }

        Task<int?> active = TotalAsync("review"), verification = TotalAsync("verification"), finished = TotalAsync("tracking");
        await Task.WhenAll(active, verification, finished);
        _counts =
        [
            new("Aktif incelemeler", active.Result, "/in-use?view=review", _view == "review" && _search.Length == 0 && _status.Length == 0, "Henüz tamamlanmamış kayıtlar"),
            new("Doğrulama bekleyen", verification.Result, "/in-use?view=verification", _view == "verification", "WASAS durumu kaynaktan doğrulanmadı"),
            new("Kapalı / tamamlanan", finished.Result, "/in-use/closed", false, "Ürünün tamamlanmış bildiği kayıtlar")
        ];
    }

    /// <summary>The completion gate in words for the guide: null when the server says it is open, "" when this screen does not know.</summary>
    private string? GuideGate => !Id.HasValue || _execution is null ? ""
        : _execution.Readiness.Available ? null : _execution.Readiness.Explanation;

    private string? GuideBadge => Id.HasValue && _execution is not null ? _execution.Readiness.Available ? "Açık" : "Kapalı" : null;

    private SoStatusBadge.BadgeTone GuideTone => GuideBadge == "Açık" ? SoStatusBadge.BadgeTone.Info : SoStatusBadge.BadgeTone.Neutral;

    private IReadOnlyList<SoTourStep> GuideTour => Id.HasValue
        ? InUseTours.Record(CanEdit, Can(Capabilities.InUseReview), _execution is null ? "durum henüz alınamadı" : _execution.Readiness.Available ? null : _execution.Readiness.Explanation)
        : InUseTours.List(Can(Capabilities.InUseRefresh));
}
