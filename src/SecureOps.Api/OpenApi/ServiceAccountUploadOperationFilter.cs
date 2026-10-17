using Microsoft.OpenApi;
using SecureOps.Api.Controllers.ServiceAccounts;
using SecureOps.Domain.ServiceAccounts;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SecureOps.Api.OpenApi;

/// <summary>
/// Describes the two usage-scan uploads (ADR-0027) as the form actually sent: <c>file</c> as binary (Swashbuckle 10 rejects
/// <c>[FromForm]</c> on an <c>IFormFile</c>, so the parameter binds from the form by default), the run statement's bounds, the optional request and the
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
        Dictionary<string, IOpenApiSchema> properties = new()
        {
            ["file"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary", Description = "Combined usage-scan JSON (Invoke-ServiceAccountUsageScan.ps1 -CombinePath)." },
            ["runStatement"] = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 5, MaxLength = 400, Description = "Where and under which authority the scan ran; no control characters." }
        };
        if (batch)
        {
            properties["accountIds"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                MinItems = 1,
                MaxItems = UsageScanBatch.MaxAccounts,
                UniqueItems = true,
                Items = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
                Description = "Repeated form field, one account id per value."
            };
        }
        else
        {
            properties["requestId"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid", Description = "Open request of this account (participant basis)." };
        }

        OpenApiSchema schema = new()
        {
            Type = JsonSchemaType.Object,
            Required = new HashSet<string>(batch ? ["file", "runStatement", "accountIds"] : ["file", "runStatement"]),
            Properties = properties
        };
        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["multipart/form-data"] = new()
                {
                    Schema = schema,
                    Encoding = batch ? new Dictionary<string, OpenApiEncoding> { ["accountIds"] = new() { Style = ParameterStyle.Form, Explode = true } } : null
                }
            }
        };
    }
}
