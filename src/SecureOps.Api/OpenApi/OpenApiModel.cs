using Microsoft.OpenApi;

namespace SecureOps.Api.OpenApi;

/// <summary>
/// Microsoft.OpenApi 2.x hands filters read-only interfaces. These helpers reach the concrete,
/// mutable model and fail loudly when generation produced something else (for example a reference),
/// so a contract change surfaces as a broken document instead of a silently skipped annotation.
/// </summary>
internal static class OpenApiModel
{
    public static OpenApiSchema Concrete(IOpenApiSchema? schema) =>
        schema as OpenApiSchema ?? throw new InvalidOperationException("Expected an inline OpenAPI schema.");

    public static OpenApiResponse Concrete(IOpenApiResponse? response) =>
        response as OpenApiResponse ?? throw new InvalidOperationException("Expected an inline OpenAPI response.");

    public static void NotNullable(IOpenApiSchema? schema)
    {
        OpenApiSchema concrete = Concrete(schema);
        concrete.Type &= ~JsonSchemaType.Null;
    }

    public static IDictionary<string, IOpenApiHeader> Headers(IOpenApiResponse? response)
    {
        OpenApiResponse concrete = Concrete(response);
        return concrete.Headers ??= new Dictionary<string, IOpenApiHeader>();
    }

    public static IDictionary<string, OpenApiMediaType> Content(IOpenApiResponse? response)
    {
        OpenApiResponse concrete = Concrete(response);
        return concrete.Content ??= new Dictionary<string, OpenApiMediaType>();
    }
}
