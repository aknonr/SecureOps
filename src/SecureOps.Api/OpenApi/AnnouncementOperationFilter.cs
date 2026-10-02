using Microsoft.OpenApi;
using SecureOps.Api.Controllers;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SecureOps.Api.OpenApi;

/// <summary>Describes the draft resource's authenticated alternate representations.</summary>
public sealed class AnnouncementOperationFilter : IOperationFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(AnnouncementsController))
        { return; }
        OpenApiResponses responses = operation.Responses ??= [];
        IList<IOpenApiParameter> parameters = operation.Parameters ?? [];
        if (context.MethodInfo.Name == nameof(AnnouncementsController.PreviewAsync))
        {
            OpenApiModel.Headers(responses["200"])["X-Announcement-Incomplete"] = new OpenApiHeader
            { Description = "Comma-separated missing-field keys; no saved version or ETag.", Schema = new OpenApiSchema { Type = JsonSchemaType.String } };
            responses["429"] = new OpenApiResponse { Description = "Actor preview limit exceeded; no automatic retry." };
        }
        foreach (IOpenApiParameter parameter in parameters.Where(p => p.Name is "page" or "pageSize"))
        {
            OpenApiSchema schema = OpenApiModel.Concrete(parameter.Schema);
            schema.Minimum = "1";
            schema.Maximum = parameter.Name == "page" ? "10000" : "100";
        }
        if (context.MethodInfo.Name is nameof(AnnouncementsController.GetAsync) or nameof(AnnouncementsController.SaveAsync))
        { OpenApiModel.Headers(responses["200"])["ETag"] = new OpenApiHeader { Description = "Quoted current draft version", Schema = new OpenApiSchema { Type = JsonSchemaType.String } }; }
        responses["default"] = new OpenApiResponse { Description = "ProblemDetails: code, fields and correlationId; 400 invalid/incomplete, 401/403 access, 404 not found, 409 conflict/asset changed, 503 unavailable." };
        IOpenApiParameter? format = parameters.FirstOrDefault(p => p.Name == "format");
        if (format is null)
        { return; }
        OpenApiModel.Concrete(format.Schema).Enum = ["draft", "html", "eml"];
        IDictionary<string, OpenApiMediaType> content = OpenApiModel.Content(responses["200"]);
        content["text/html"] = new OpenApiMediaType { Schema = new OpenApiSchema { Type = JsonSchemaType.String } };
        content["message/rfc822"] = new OpenApiMediaType { Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" } };
    }
}
