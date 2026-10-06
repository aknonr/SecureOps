using Microsoft.OpenApi.Models;
using SecureOps.Api.Controllers.ServiceAccounts;
using SecureOps.Domain.ServiceAccounts;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SecureOps.Api.OpenApi;

/// <summary>
/// Describes the two usage-scan uploads (ADR-0027) as the form actually sent: <c>file</c> as binary (Swashbuckle 6 otherwise
/// expands <c>[FromForm] IFormFile</c> into its own properties), the run statement's bounds, the optional request and the
/// 1–20 distinct account ids. The server still validates every field; this only documents it.
/// </summary>
public sealed class ServiceAccountUploadOperationFilter : IOperationFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(ServiceAccountsController)
            || context.MethodInfo.Name is not (nameof(ServiceAccountsController.UsageScanUploadAsync) or nameof(ServiceAccountsController.UsageScanBatchUploadAsync)))
        { return; }
        bool batch = context.MethodInfo.Name == nameof(ServiceAccountsController.UsageScanBatchUploadAsync);
        OpenApiSchema schema = new()
        {
            Type = "object",
            Required = new HashSet<string>(batch ? ["file", "runStatement", "accountIds"] : ["file", "runStatement"]),
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["file"] = new() { Type = "string", Format = "binary", Description = "Combined usage-scan JSON (Invoke-ServiceAccountUsageScan.ps1 -CombinePath)." },
                ["runStatement"] = new() { Type = "string", MinLength = 5, MaxLength = 400, Description = "Where and under which authority the scan ran; no control characters." }
            }
        };
        if (batch)
        {
            schema.Properties["accountIds"] = new()
            {
                Type = "array",
                MinItems = 1,
                MaxItems = UsageScanBatch.MaxAccounts,
                UniqueItems = true,
                Items = new() { Type = "string", Format = "uuid" },
                Description = "Repeated form field, one account id per value."
            };
        }
        else
        {
            schema.Properties["requestId"] = new() { Type = "string", Format = "uuid", Description = "Open request of this account (participant basis)." };
        }

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["multipart/form-data"] = new()
                {
                    Schema = schema,
                    Encoding = batch ? new Dictionary<string, OpenApiEncoding> { ["accountIds"] = new() { Style = ParameterStyle.Form, Explode = true } } : []
                }
            }
        };
    }
}
