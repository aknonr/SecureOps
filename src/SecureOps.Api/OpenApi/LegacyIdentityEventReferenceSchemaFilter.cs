using Microsoft.OpenApi;
using SecureOps.Shared.Contracts.Identity;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SecureOps.Api.OpenApi;

/// <summary>Marks retained read-only identity event references as deprecated for new clients.</summary>
public sealed class LegacyIdentityEventReferenceSchemaFilter : ISchemaFilter
{
    /// <inheritdoc />
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        // Swashbuckle also passes each use site as a $ref; only the component definition is annotated.
        if ((context.Type != typeof(IdentityLookupRequest)
            && context.Type != typeof(BulkIdentityLookupRequest)) || schema is OpenApiSchemaReference)
        {
            return;
        }

        MarkDeprecated(schema, "alertId");
        MarkDeprecated(schema, "turuncuhatEvtId");
    }

    private static void MarkDeprecated(IOpenApiSchema schema, string propertyName)
    {
        if (schema.Properties?.TryGetValue(propertyName, out IOpenApiSchema? property) == true)
        {
            OpenApiSchema concrete = OpenApiModel.Concrete(property);
            concrete.Deprecated = true;
            concrete.Description = "Deprecated optional legacy event reference. New read-only clients should omit it.";
        }
    }
}
