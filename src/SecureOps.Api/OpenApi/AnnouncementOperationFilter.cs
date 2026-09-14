using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
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
        if (context.MethodInfo.Name == nameof(AnnouncementsController.PreviewAsync))
        {
            operation.Responses["200"].Headers["X-Announcement-Incomplete"] = new OpenApiHeader
            { Description = "Comma-separated missing-field keys; no saved version or ETag.", Schema = new() { Type = "string" } };
            operation.Responses["429"] = new OpenApiResponse { Description = "Actor preview limit exceeded; no automatic retry." };
        }
        foreach (OpenApiParameter parameter in operation.Parameters.Where(p => p.Name is "page" or "pageSize"))
        {
            parameter.Schema.Minimum = 1;
            parameter.Schema.Maximum = parameter.Name == "page" ? 10000 : 100;
        }
        if (context.MethodInfo.Name is nameof(AnnouncementsController.GetAsync) or nameof(AnnouncementsController.SaveAsync))
        { operation.Responses["200"].Headers["ETag"] = new OpenApiHeader { Description = "Quoted current draft version", Schema = new() { Type = "string" } }; }
        operation.Responses["default"] = new OpenApiResponse { Description = "ProblemDetails: code, fields and correlationId; 400 invalid/incomplete, 401/403 access, 404 not found, 409 conflict/asset changed, 503 unavailable." };
        OpenApiParameter? format = operation.Parameters.FirstOrDefault(p => p.Name == "format");
        if (format is null)
        { return; }
        format.Schema.Enum = [new OpenApiString("draft"), new OpenApiString("html"), new OpenApiString("eml")];
        operation.Responses["200"].Content["text/html"] = new OpenApiMediaType { Schema = new() { Type = "string" } };
        operation.Responses["200"].Content["message/rfc822"] = new OpenApiMediaType { Schema = new() { Type = "string", Format = "binary" } };
    }
}
