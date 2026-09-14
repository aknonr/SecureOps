using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SecureOps.Tests.Integration.Api;

public sealed class AnnouncementSourceConfigurationTests
{
    [Theory]
    [InlineData(false, false, "AnnouncementsDisabled")]
    [InlineData(true, false, "AnnouncementSourceDisabled")]
    [InlineData(true, true, "AnnouncementSourceJobHostUnavailable")]
    public async Task Submit_DisabledCompositionFailsClosedBeforeSql(bool drafts, bool source, string error)
    {
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("DemoAuth:Enabled", "true");
            builder.UseSetting("Access:DemoCompatibilityEnabled", "true");
            builder.UseSetting("Audit:Provider", "InMemory");
            builder.UseSetting("IdentityLookup:Provider", "Mock");
            builder.UseSetting("Announcements:Enabled", drafts.ToString());
            builder.UseSetting("AnnouncementSource:Enabled", source.ToString());
            builder.UseSetting("Hangfire:Enabled", "false");
            builder.UseSetting("ConnectionStrings:SecureOpsDb", "");
        });
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", "platform-admin");
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/announcements/" + Guid.NewGuid() + "/source/jobs",
            new { profile = "NonProd", ocoReference = "OCO-TEST", submissionKey = "test-key" });
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be(error);
    }
}
