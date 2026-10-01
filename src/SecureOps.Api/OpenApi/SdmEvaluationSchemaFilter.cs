using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Shared.Contracts.OperationalRecords;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SecureOps.Api.OpenApi;

/// <summary>Preserves exact nullability for the additive SDM v1 response only.</summary>
public sealed class SdmEvaluationSchemaFilter : ISchemaFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(OperationalRecordResponse))
        {
            return;
        }
        schema.Properties["recommendedClassification"] = new OpenApiSchema
        {
            Type = "integer",
            Nullable = true,
            Enum = Enum.GetValues<OperationalRecordClassification>()
                .Select(value => (IOpenApiAny)new OpenApiInteger((int)value)).Append(new OpenApiNull()).ToList()
        };
        schema.Properties["reasonCodes"].Nullable = false;
        schema.Properties["blockingConditions"].Nullable = false;
    }
}
