WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
WebApplication app = builder.Build();

app.MapGet("/", () => Results.Redirect("/health"));
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.Run();
