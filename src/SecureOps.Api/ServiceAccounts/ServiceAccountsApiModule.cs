using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Api.Middleware;
using SecureOps.Api.Security;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Api.ServiceAccounts;

/// <summary>API wiring for the Service Accounts module: one additive call from Program.cs.</summary>
public static class ServiceAccountsApiModule
{
    /// <summary>Module-owned rate limit for list exports (registered here; existing platform policies are unchanged).</summary>
    public const string ExportRateLimit = "ServiceAccountExport";

    /// <summary>Registers module services and capability policies (reusing the platform capability handler).</summary>
    public static IServiceCollection AddServiceAccountsApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddServiceAccounts(configuration);
        services.Configure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(options => options.AddPolicy(ExportRateLimit,
            context => ApiRateLimits.Partition(context, ExportRateLimit, new SecureOps.Shared.Configuration.OperationRateLimitOptions { PermitLimit = 3, WindowSeconds = 60 })));
        services.Configure<AuthorizationOptions>(options =>
        {
            foreach ((string policy, string capability) in ServiceAccountPolicies.Map)
            {
                options.AddPolicy(policy, builder => builder.RequireAuthenticatedUser().AddRequirements(new CapabilityRequirement(capability)));
            }
        });
        return services;
    }
}

/// <summary>Maps module results to safe ProblemDetails; 409 includes only data the caller may already read.</summary>
public static class ServiceAccountReplies
{
    /// <summary>Success → 200; failure → mapped ProblemDetails.</summary>
    public static ActionResult<T> Reply<T>(ControllerBase controller, SaResult<T> result)
    {
        if (result.IsSuccess)
        {
            return controller.Ok(result.Value);
        }

        (int status, string stage, bool retryable, string title) = result.ErrorCode switch
        {
            SaErrors.NotFound => (404, "service-accounts", false, "Kayıt bulunamadı veya erişim kapsamınızda değil."),
            SaErrors.Forbidden => (403, "authorization", false, "Bu işlem için yetkiniz veya kapsamınız yok."),
            SaErrors.Invalid => (400, "validation", false, "Girilen bilgi geçersiz."),
            SaErrors.ImportFile => (400, "import-file", false, "Dosya kabul edilmedi."),
            SaErrors.IdempotencyKey => (400, "validation", false, "İşlem anahtarı eksik."),
            SaErrors.Conflict => (409, "concurrency", true, "Kayıt siz düzenlerken değişti; güncel değerleri inceleyip yeniden kaydedin."),
            SaErrors.PreviewStale => (409, "import-preview", true, "Önizlemeden sonra veri değişti; önizlemeyi yenileyin."),
            SaErrors.DecisionsRequired => (409, "import-decisions", false, "Karar bekleyen satırlar var."),
            SaErrors.AlreadyImported => (409, "import-replay", false, "Bu dosya aynı dönem ve kapsam için zaten aktarıldı."),
            SaErrors.NotConfigured => (503, "configuration", false, "Servis hesapları modülü bu ortamda etkin değil."),
            _ => (503, "persistence", true, "Kayıt deposuna şu an ulaşılamıyor.")
        };
        ObjectResult problem = OperationalProblemDetails.Create(status, result.ErrorCode!, title, controller.HttpContext.TraceIdentifier, stage, retryable);
        var details = (ProblemDetails)problem.Value!;
        if (result.Field is { } field)
        {
            details.Extensions["field"] = field;
        }

        if (result.Current is { } current)
        {
            details.Extensions["current"] = current;
        }

        return problem;
    }

    /// <summary>Operation context from the authenticated request.</summary>
    public static AccessOperationContext Context(ControllerBase controller) =>
        new(controller.User.Identity?.Name ?? "unknown", System.Diagnostics.Activity.Current?.Id ?? controller.HttpContext.TraceIdentifier,
            controller.HttpContext.Connection.RemoteIpAddress?.ToString());
}
