using Microsoft.OpenApi.Models;
using SecureOps.Shared.Contracts.Identity;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SecureOps.Api.OpenApi;

/// <summary>Marks retained read-only identity event references as deprecated for new clients.</summary>
public sealed class LegacyIdentityEventReferenceSchemaFilter : ISchemaFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(IdentityLookupRequest)
            && context.Type != typeof(BulkIdentityLookupRequest))
        {
            return;
        }

        MarkDeprecated(schema, "alertId");
        MarkDeprecated(schema, "turuncuhatEvtId");
    }

    private static void MarkDeprecated(OpenApiSchema schema, string propertyName)
    {
        if (schema.Properties.TryGetValue(propertyName, out OpenApiSchema? property))
        {
            property.Deprecated = true;
            property.Description = "Deprecated optional legacy event reference. New read-only clients should omit it.";
        }
    }
}
