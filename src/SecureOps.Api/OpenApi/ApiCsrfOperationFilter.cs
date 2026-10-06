using Microsoft.OpenApi.Models;
using SecureOps.Api.Security;
using SecureOps.Shared.Contracts.Api;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SecureOps.Api.OpenApi;

/// <summary>Documents the global unsafe-request intent contract without changing authentication.</summary>
public sealed class ApiCsrfOperationFilter : IOperationFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (ApiCsrfPolicy.IsSafeMethod(context.ApiDescription.HttpMethod ?? string.Empty))
        {
            return;
        }

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = ApiCsrf.HeaderName,
            In = ParameterLocation.Header,
            Required = true,
            Description = "Required value: 1. Not authentication. Browser origins must be explicitly allowed; cross-site Fetch Metadata is rejected (ADR-0029).",
            Schema = new OpenApiSchema { Type = "string", Enum = [new Microsoft.OpenApi.Any.OpenApiString(ApiCsrf.HeaderValue)] }
        });
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Access denied or ApiCsrfRejected (ProblemDetails)." });
    }
}
