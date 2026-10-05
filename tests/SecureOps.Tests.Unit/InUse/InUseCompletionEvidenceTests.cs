using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecureOps.Infrastructure;
using SecureOps.Infrastructure.OperationalRecords;

namespace SecureOps.Tests.Unit.InUse;

public sealed class InUseCompletionEvidenceTests
{
    [Theory]
    [InlineData(0, "NoneReturned")]
    [InlineData(1, "OneCandidateNotApproved")]
    [InlineData(2, "Ambiguous")]
    public async Task Probe_UsesKnownSelectorsAndAliasedEvidence_NeverWrites(int count, string expected)
    {
        using var handler = new EvidenceHandler(count);
        using ServiceProvider provider = Create(handler);
        var client = (TuruncuHatOperationalRecordClient)provider.GetRequiredService<IOperationalRecordClient>();
        JsonElement result = await client.DiagnoseCompletionAsync("000100", TestContext.Current.CancellationToken);
        handler.Paths.Should().Equal("/api/login", "/api/query", "/api/query");
        result.GetProperty("ActivitySelection").GetString().Should().Be(expected);
        result.GetProperty("WritesPerformed").GetBoolean().Should().BeFalse();
        result.GetRawText().Should().NotContain("000100").And.NotContain("OR-000100").And.NotContain("synthetic-session");
        result.GetProperty("Attachment").GetString().Should().Be("NotQueriedContractMissing");
        result.GetProperty("FinalOrState").GetString().Should().Be("NotQueriedContractMissing");
        if (count > 0)
        {
            result.GetProperty("Root")[0].GetProperty("Alias").GetString().Should().Be(
                result.GetProperty("Activities")[0][1].GetProperty("Alias").GetString());
        }
    }

    [Theory]
    [InlineData("mismatch")]
    [InlineData("duplicate")]
    [InlineData("pages")]
    [InlineData("count")]
    [InlineData("error")]
    [InlineData("oversized")]
    public async Task Probe_RejectsUntrustedOrPartialRoot_BeforeReadingActivities(string mode)
    {
        using var handler = new EvidenceHandler(1, mode);
        using ServiceProvider provider = Create(handler);
        var client = (TuruncuHatOperationalRecordClient)provider.GetRequiredService<IOperationalRecordClient>();
        await FluentActions.Awaiting(() => client.DiagnoseCompletionAsync("000100", default)).Should().ThrowAsync<Exception>();
        handler.Paths.Should().HaveCount(2);
    }

    [Fact]
    public async Task Probe_WriteFlagsOrNonNumericIdentity_RejectBeforeAuthentication()
    {
        using var handler = new EvidenceHandler(1);
        using ServiceProvider provider = Create(handler, writes: true);
        var client = (TuruncuHatOperationalRecordClient)provider.GetRequiredService<IOperationalRecordClient>();
        await FluentActions.Awaiting(() => client.DiagnoseCompletionAsync("000100", default)).Should().ThrowAsync<InvalidDataException>();
        handler.Paths.Should().BeEmpty();
        using ServiceProvider readOnly = Create(handler);
        client = (TuruncuHatOperationalRecordClient)readOnly.GetRequiredService<IOperationalRecordClient>();
        await FluentActions.Awaiting(() => client.DiagnoseCompletionAsync("100 OR 1=1", default)).Should().ThrowAsync<InvalidDataException>();
        handler.Paths.Should().BeEmpty();
    }

    private static ServiceProvider Create(HttpMessageHandler handler, bool writes = false)
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OperationalRecords:SourceProvider"] = "TuruncuHat",
            ["OperationalRecords:ReadOnlyIntegrationMode"] = "true",
            ["OperationalRecords:ControlledTestWritesEnabled"] = writes.ToString(),
            ["OperationalRecords:SourceCloseEnabled"] = "false",
            ["TuruncuHat:BaseUrl"] = "https://source.invalid/api/",
            ["TuruncuHat:Authorization"] = "synthetic-only",
            ["TuruncuHat:Username"] = "synthetic-only",
            ["TuruncuHat:Password"] = "synthetic-only",
            ["TuruncuHat:TenantId"] = "1",
            ["TuruncuHat:SessionLifetimeSeconds"] = "60"
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSecureOpsInfrastructure(config);
        services.AddHttpClient("TuruncuHat").ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private sealed class EvidenceHandler(int count, string mode = "") : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            if (Paths.Count == 1)
            { return Reply("""{"LoginResult":"ok|synthetic-session"}"""); }
            request.RequestUri.AbsolutePath.Should().Be("/api/query");
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            JsonElement body = json.RootElement.GetProperty("req");
            if (Paths.Count == 2)
            {
                body.GetProperty("BaseObject").GetString().Should().Be("SMSS_oRFF");
                body.GetProperty("Selects").EnumerateArray().Select(x => x.GetString()).Should().Equal("id", "p_code", "p_emb_dynamic_case_orff");
                body.GetProperty("Filters")[0].GetString().Should().Contain("#%id%#=000100").And.Contain("4241").And.Contain("68");
                var cells = new List<object> { new { Key = "SET.id", Value = mode == "mismatch" ? "999" : "000100" },
                    new { Key = "SET.p_code", Value = "OR-000100" }, new { Key = "SET.p_emb_dynamic_case_orff", Value = "000200" } };
                if (mode == "duplicate")
                { cells.Add(new { Key = "SET.id", Value = "000100" }); }
                if (mode == "oversized")
                { cells.Add(new { Key = "KEY.p_emb_dynamic_case_orff", Value = new string('x', 8001) }); }
                return Reply(JsonSerializer.Serialize(new
                {
                    QueryResult = new
                    {
                        Items = new[] { cells },
                        MaxPages = mode == "pages" ? 2 : 1,
                        RecordCount = mode == "count" ? 2 : 1,
                        ErrorNo = mode == "error" ? 9 : 0
                    }
                }));
            }
            body.GetProperty("BaseObject").GetString().Should().Be("BPM_Actvty");
            body.GetProperty("Filters")[0].GetString().Should().Be(
                "(#%m_actvty_task_model%#=103626 OR #%m_actvty_task_model%#=103627) AND #%m_status%#=1 AND #%m_group%#=68 AND #%m_process.m_main_object_id%#=000100");
            return Reply(JsonSerializer.Serialize(new
            {
                QueryResult = new
                {
                    Items = Enumerable.Range(0, count).Select(i => new[] {
                new { Key = "SET.id", Value = (300 + i).ToString() }, new { Key = "SET.m_process.m_main_object_id", Value = "000100" } })
                }
            }));
        }
        private static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    }
}
