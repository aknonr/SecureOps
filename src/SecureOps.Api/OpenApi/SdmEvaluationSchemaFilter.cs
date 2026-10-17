using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Shared.Contracts.OperationalRecords;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SecureOps.Api.OpenApi;

/// <summary>Preserves exact nullability for the additive SDM v1 response only.</summary>
public sealed class SdmEvaluationSchemaFilter : ISchemaFilter
{
    /// <inheritdoc />
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        // Swashbuckle also passes each use site as a $ref; only the component definition is annotated.
        if (context.Type != typeof(OperationalRecordResponse) || schema is OpenApiSchemaReference)
        {
            return;
        }
        IDictionary<string, IOpenApiSchema> properties = OpenApiModel.Concrete(schema).Properties!;
        properties["recommendedClassification"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Integer | JsonSchemaType.Null,
            // The v1 contract lists null as an allowed value; JSON null has no JsonNode instance.
            Enum = [.. Enum.GetValues<OperationalRecordClassification>().Select(value => (JsonNode)JsonValue.Create((int)value)).Append(null!)]
        };
        OpenApiModel.NotNullable(properties["reasonCodes"]);
        OpenApiModel.NotNullable(properties["blockingConditions"]);
    }
}
