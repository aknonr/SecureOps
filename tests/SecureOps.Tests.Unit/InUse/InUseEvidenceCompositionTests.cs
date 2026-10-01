using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecureOps.Infrastructure;
using SecureOps.Infrastructure.OperationalRecords;

namespace SecureOps.Tests.Unit.InUse;

public sealed class InUseEvidenceCompositionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task JsonConfigurationAndRealSession_EmptyDictionary_ReadOnlyLegacyFields(int count)
    {
        using var configStream = new MemoryStream(Encoding.UTF8.GetBytes("""
            {"OperationalRecords":{"SourceProvider":"TuruncuHat","ReadOnlyIntegrationMode":true,
            "ControlledTestWritesEnabled":false,"SourceCloseEnabled":false},
            "TuruncuHat":{"BaseUrl":"https://source.invalid/api/","Authorization":"synthetic-only",
            "Username":"synthetic-only","Password":"synthetic-only","TenantId":1,"SessionLifetimeSeconds":60}}
            """));
        IConfiguration config = new ConfigurationBuilder().AddJsonStream(configStream).Build();
        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSecureOpsInfrastructure(config);
        using var handler = new SyntheticHandler(count);
        services.AddHttpClient("TuruncuHat").ConfigurePrimaryHttpMessageHandler(() => handler);
        using ServiceProvider provider = services.BuildServiceProvider();
        var client = (TuruncuHatOperationalRecordClient)provider.GetRequiredService<IOperationalRecordClient>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        JsonElement evidence = await client.DiagnoseAsync("100", new Dictionary<string, string>(), timeout.Token);

        handler.Paths.Should().Equal("/api/login", "/api/query", "/api/query");
        handler.Fields.Should().HaveCount(15).And.OnlyContain(s => s.StartsWith("(LCSIMS_ServiceInstance)m_rid.", StringComparison.Ordinal));
        handler.Fields.Should().Contain("(LCSIMS_ServiceInstance)m_rid.c_new_SI_major_project.id");
        evidence.GetProperty("ServiceItems").GetArrayLength().Should().Be(count);
        evidence.GetProperty("AffectedAssets").GetString().Should().Be("NotQueried");
        evidence.GetRawText().Should().NotContain("synthetic-only").And.NotContain("OR-100").And.NotContain("server-");
        if (count > 0)
        {
            evidence.GetProperty("ServiceItems")[0].GetArrayLength().Should().Be(15);
            evidence.GetProperty("ServiceItems")[0][4].GetProperty("Alias").GetString().Should().Be(
                evidence.GetProperty("ServiceItems")[1][4].GetProperty("Alias").GetString());
        }
    }

    private sealed class SyntheticHandler(int count) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public string[] Fields { get; private set; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Method.Should().Be(HttpMethod.Post);
            Paths.Add(request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (Paths.Count == 1)
            {
                return Reply("""{"LoginResult":"ok|synthetic-only"}""");
            }
            JsonElement query = body.RootElement.GetProperty("req");
            if (Paths.Count == 2)
            {
                query.GetProperty("Filters")[0].GetString().Should().Be("#%id%#=100 AND #%m_active%#='True' AND #%p_dcc%# IN (4241) AND #%p_rel_group%# IN (68)");
                return Reply("""
                    {"QueryResult":{"Items":[[{"Key":"SET.id","Value":"100"},
                    {"Key":"SET.p_code","Value":"OR-100"},{"Key":"SET.p_name","Value":"synthetic-only"},
                    {"Key":"SET.p_description","Value":"synthetic-only"},{"Key":"KEY.p_rel_requester","Value":"synthetic-only"}]]}}
                    """);
            }
            Paths.Count.Should().Be(3);
            query.GetProperty("Filters")[0].GetString().Should().Be("#%m_tid%#=100049 and #%m_lid%#=100");
            Fields = query.GetProperty("Selects").EnumerateArray().Select(v => v.GetString()!).ToArray();
            return Reply(JsonSerializer.Serialize(new
            {
                QueryResult = new
                {
                    Items = Enumerable.Range(0, count).Select(i =>
                Fields.Select((f, n) => new { Key = "SET." + f, Value = n == 4 ? "synthetic-only" : $"server-{i}-field-{n}" }).ToArray()).ToArray()
                }
            }));
        }
        private static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    }
}
