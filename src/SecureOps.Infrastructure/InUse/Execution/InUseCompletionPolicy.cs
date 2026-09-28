using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse.Execution;

/// <summary>Independent write fences; fixture activation is allowed only in isolated local compositions.</summary>
public sealed class InUseCompletionPolicy(IOptions<InUseCompletionOptions> options,
    IOptions<OperationalRecordsOptions> operations, string environment)
{
    /// <summary>Secret-free binding prevents a queued intent surviving an unnoticed provider/fence change.</summary>
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        ExecutionContract = "WasasActivityManual-v2",
        options.Value.Enabled,
        options.Value.Provider,
        options.Value.FixtureDirectory,
        options.Value.TimeoutSeconds,
        operations.Value.ReadOnlyIntegrationMode,
        operations.Value.ControlledTestWritesEnabled,
        operations.Value.SourceCloseEnabled,
        environment
    })));

    /// <summary>Normal deployments remain disabled; corporate missing contracts cannot be bypassed by a boolean.</summary>
    public InUseExecutionReadiness Readiness(InUseSource source)
    {
        if (source.WasasActivity?.Value == "Completed" || source.Lifecycle?.Value == "Closed")
        { return new(false, "TrackingOnly", "Kaynakta tamamlanmış iş yalnız izlenir. Başka ekibin aktivitesi onaylanmaz."); }
        if (!options.Value.Enabled)
        {
            return new(false, "Disabled", source.Synthetic
                ? "Tamamlama özelliği yönetici tarafından kapalı. Excel'i WASAS'a arşivleyip indirebilirsiniz."
                : "WASAS adımı gönderimi kapalı. Ek içeriği doğrulama, gerekli alan eşlemesi ve koşullu görev güncelleme sözleşmeleri tamamlanmalı. Şimdilik raporu arşivleyip indirin; kaynak sistemde yalnız WASAS görevini onaylayın. OR'nin açık kalması sonraki ekibin işiyle uyumludur.");
        }
        if (options.Value.Provider == "Fixture" && environment is "Test" or "Development" or "Demo"
            && source.Synthetic && source.IdentityScope?.StartsWith("simulation:", StringComparison.Ordinal) == true
            && Path.IsPathFullyQualified(options.Value.FixtureDirectory))
        { return new(true, "SyntheticOnly", "Yalnız sentetik yerel kaynak: ek doğrulandıktan sonra WASAS onayı iletilir; OR kapanışı ve sonraki ekip ayrı izlenir."); }
        if (operations.Value.ReadOnlyIntegrationMode || !operations.Value.ControlledTestWritesEnabled || !operations.Value.SourceCloseEnabled)
        { return new(false, "WriteFence", "Kaynak yazma kontrolleri kapalı. Arşivleme ve indirme kullanılabilir."); }
        return options.Value.Provider == "TuruncuHat"
            ? new(false, "SourceContractsMissing", "Dinamik vaka hedefi ve koşullu güncelleme, tekil görev eşzamanlılığı ve ek kimliği/içerik doğrulaması için kaynak sözleşmesi eksik. Son OR durumu manuel kontrol edilebilir; arşivleme kullanılabilir.")
            : new(false, "ProviderUnavailable", "Onaylı tamamlama sağlayıcısı yok. Arşivleme kullanılabilir.");
    }
}
