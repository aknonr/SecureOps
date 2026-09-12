using System.Net;
using System.Text.Json;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = null);
int port = builder.Configuration.GetValue<int>("Port");
string sourceId = builder.Configuration["SourceId"] ?? "";
if (port is < 1024 or > 65535 || !long.TryParse(sourceId, out long id) || id is < 930000000 or > 939999999)
{
    throw new InvalidOperationException("Explicit loopback port and synthetic source range required.");
}
builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(port, endpoint => endpoint.UseHttps()));
WebApplication app = builder.Build();
string mode = "current";
int logins = 0, targets = 0, queries = 0, rejected = 0;
object Cell(string key, string? value) => new { Key = key, Value = value };
app.Use(async (context, next) =>
{
    if (context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address))
    {
        context.Response.StatusCode = 403;
        return;
    }
    await next(context);
});
app.MapGet("/stats", () => new { logins, targets, queries, rejected, mode });
app.MapPost("/mode/{value}", (string value) =>
{
    if (value is not ("current" or "partial" or "outage"))
    {
        return Results.BadRequest();
    }
    mode = value;
    return Results.NoContent();
});
app.MapPost("/source/login", async (HttpRequest request) =>
{
    using JsonDocument body = await JsonDocument.ParseAsync(request.Body);
    JsonElement credentials = body.RootElement.GetProperty("req");
    if (request.Headers.Authorization != "fixture-only" || credentials.GetProperty("Username").GetString() != "fixture"
        || credentials.GetProperty("Password").GetString() != "fixture" || credentials.GetProperty("TenantId").GetInt32() != 1)
    {
        rejected++;
        return Results.Unauthorized();
    }
    logins++;
    return Results.Json(new { LoginResult = "fixture|local-session-only" });
});
app.MapPost("/source/Query", async (HttpRequest request) =>
{
    queries++;
    if (mode == "outage")
    {
        return Results.StatusCode(503);
    }
    using JsonDocument body = await JsonDocument.ParseAsync(request.Body);
    JsonElement query = body.RootElement.GetProperty("req");
    if (request.Headers.Authorization != "fixture-only" || query.GetProperty("SessionID").GetString() != "fixture|local-session-only"
        || query.GetProperty("Filters").GetArrayLength() != 1)
    {
        rejected++;
        return Results.Unauthorized();
    }
    string filter = query.GetProperty("Filters")[0].GetString()!;
    object[][] rows;
    if (filter == "#%m_active%#='True' AND #%p_dcc%# IN (4241) AND #%p_rel_group%# IN (68)")
    {
        rows = [[Cell("SET.id", sourceId), Cell("SET.p_code", "OR-" + sourceId),
            Cell("SET.p_name", "Synthetic RFC reporter acceptance"), Cell("SET.p_description", "Synthetic only"),
            Cell("KEY.p_rel_requester", "Synthetic parent")]];
    }
    else if (filter == $"#%m_tid%#=100049 and #%m_lid%#={id}")
    {
        rows = Enumerable.Range(1, 4).Select(i => new object[] {
            Cell("SET.(LCSIMS_ServiceInstance)m_rid.id", (92000 + i).ToString()),
            Cell("SET.(LCSIMS_ServiceInstance)m_rid.p_name", "synthetic-rfc-" + i),
            Cell("SET.(LCSIMS_ServiceInstance)m_rid.c_rfc_record", i == 4 ? null : i == 3 ? "OR-201" : "OR-200") }).Reverse().ToArray();
    }
    else if (filter is "#%p_code%#='OR-200'" or "#%p_code%#='OR-201'")
    {
        targets++;
        bool second = filter == "#%p_code%#='OR-201'";
        if (mode == "partial" && second)
        {
            return Results.StatusCode(403);
        }
        rows = [[Cell("SET.p_rel_requester", second ? "801" : "800"),
            Cell("KEY.p_rel_requester", second ? "Sentetik &#350;ah&#305;s 201 &amp;lt;b&amp;gt;" : "Sentetik &#350;ah&#305;s 200 &lt;b&gt;"),
            Cell("SET.id", second ? "201" : "200"), Cell("SET.p_code", second ? "OR-201" : "OR-200"), Cell("SET.m_active", "False")]];
    }
    else
    {
        rejected++;
        return Results.BadRequest();
    }
    return Results.Json(new { QueryResult = new { Items = rows } });
});
app.MapFallback(() => { rejected++; return Results.StatusCode(403); });
await app.RunAsync();
