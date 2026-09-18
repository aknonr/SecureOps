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
        if (!options.Value.Enabled)
        {
            return new(false, "Disabled", source.Synthetic
                ? "Tamamlama özelliği yönetici tarafından kapalı. Excel'i WASAS'a arşivleyip indirebilirsiniz."
                : "Tamamlama özelliği kapalı; ayrıca bu sürümün gerçek kaynak adaptörü ve ek/OR sonuç doğrulaması tamamlanmadı. Yalnız ayar açılması yeterli değildir. Arşivden indir kullanılabilir; entegrasyon sorumlusu eksik kaynak sözleşmelerini doğrulamalı.");
        }
        if (options.Value.Provider == "Fixture" && environment is "Test" or "Development" or "Demo"
            && source.Synthetic && source.IdentityScope?.StartsWith("simulation:", StringComparison.Ordinal) == true
            && Path.IsPathFullyQualified(options.Value.FixtureDirectory))
        { return new(true, "SyntheticOnly", "Yalnız sentetik yerel kaynak: rapor ekleme, görev ve OR durumu ayrı doğrulanır."); }
        if (operations.Value.ReadOnlyIntegrationMode || !operations.Value.ControlledTestWritesEnabled || !operations.Value.SourceCloseEnabled)
        { return new(false, "WriteFence", "Kaynak yazma kontrolleri kapalı. Arşivleme ve indirme kullanılabilir."); }
        return options.Value.Provider == "TuruncuHat"
            ? new(false, "SourceContractsMissing", "Dinamik vaka kimliği, ek kimliği/özet doğrulaması ve yetkili OR son-durum okuması için kaynak sözleşmesi eksik. Arşivleme kullanılabilir.")
            : new(false, "ProviderUnavailable", "Onaylı tamamlama sağlayıcısı yok. Arşivleme kullanılabilir.");
    }
}
