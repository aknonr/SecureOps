using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SecureOps.Infrastructure.InUse;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.InUse;

public sealed class InUseAdapterTests
{
    private const string _row = """
        [{"Key":"KEY.p_rel_requester","Value":"Synthetic requester"},{"Key":"SET.p_description","Value":"Synthetic description"},
        {"Key":"SET.p_code","Value":"OR-100"},{"Key":"SET.p_rel_requester","Value":"888"},
        {"Key":"SET.p_name","Value":"Synthetic title"},{"Key":"num","Value":"1"},{"Key":"SET.id","Value":"100"}]
        """;

    [Theory]
    [InlineData("p_password")]
    [InlineData("p_rfc.id")]
    [InlineData("p_user OR 1=1")]
    public async Task Diagnostic_UnboundedOrSensitiveDictionary_RefusesBeforeTransport(string selector)
    {
        using var handler = new Handler("{}");
        await FluentActions.Awaiting(() => Client(handler).DiagnoseAsync("100",
            new Dictionary<string, string> { ["Virtual PC User"] = selector }, CancellationToken.None)).Should().ThrowAsync<InvalidDataException>();
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task Diagnostic_ExactScopeAndAliases_DoNotResolveUnknownMappings(int count)
    {
        string relations = JsonSerializer.Serialize(new
        {
            QueryResult = new
            {
                Items = Enumerable.Range(0, count).Select(i => new[] {
            new { Key = "SET.synthetic_id", Value = $"item-{i}" }, new { Key = "KEY.synthetic_user", Value = i == 0 ? "Synthetic requester" : "different user" },
            new { Key = "SET.synthetic_rfc", Value = "OR-OTHER" } }).ToArray()
            }
        });
        using var handler = new Handler("{\"QueryResult\":{\"Items\":[" + _row + "]}}", relations);
        JsonElement report = await Client(handler).DiagnoseAsync("100", new Dictionary<string, string>
        { ["Virtual PC User"] = "p_synthetic_user", ["RFC Kaydı"] = "p_synthetic_rfc" }, CancellationToken.None);
        report.GetProperty("ServiceItems").GetArrayLength().Should().Be(count);
        report.GetProperty("AffectedAssets").GetString().Should().Be("NotQueried");
        report.ToString().Should().NotContain("Synthetic requester").And.NotContain("OR-OTHER");
        if (count > 0)
        {
            report.GetProperty("Root")[0][0].GetProperty("Alias").GetString().Should()
                .Be(report.GetProperty("ServiceItems")[0][1].GetProperty("Alias").GetString());
        }
        handler.Requests.Should().HaveCount(2).And.OnlyContain(r => r.Path == "/query");
        handler.Requests[0].Filter.Should().Contain("#%id%#=100").And.Contain("IN (4241)");
        handler.Requests[1].Filter.Should().Be("#%m_tid%#=100049 and #%m_lid%#=100");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("100 OR 1=1")]
    [InlineData("-1")]
    public async Task Diagnostic_InvalidIdentity_MakesNoRequest(string id)
    {
        using var handler = new Handler("{}");
        await FluentActions.Awaiting(() => Client(handler).DiagnoseAsync(id, CancellationToken.None)).Should().ThrowAsync<InvalidDataException>();
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"QueryResult\":{\"Items\":[[{\"Value\":\"no-key\"}]]}}")]
    [InlineData("{\"QueryResult\":{\"Items\":[[{\"Key\":\"SET.session\",\"Value\":\"do-not-return\"}]]}}")]
    public async Task Diagnostic_MalformedOrSensitiveKey_StopsWithoutRawOutput(string related)
    {
        using var handler = new Handler("{\"QueryResult\":{\"Items\":[" + _row + "]}}", related);
        await FluentActions.Awaiting(() => Client(handler).DiagnoseAsync("100", CancellationToken.None)).Should().ThrowAsync<Exception>();
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Discovery_ReorderedExpandedKeys_SeparateScopeAndUnresolvedRelationships()
    {
        using var handler = new Handler("{\"QueryResult\":{\"Items\":[" + _row + "]}}");
        TuruncuHatOperationalRecordClient client = Client(handler);
        InUseBatch result = await client.DiscoverAsync(CancellationToken.None);
        result.Complete.Should().BeFalse();
        result.Records.Single().Requester.Value.Should().Be("Synthetic requester");
        result.Records.Single().ServiceOwner.Value.Should().BeNull();
        result.Records.Single().Servers.Should().BeEmpty();
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Path.Should().Be("/query");
        handler.Requests[0].Filter.Should().Be("#%m_active%#='True' AND #%p_dcc%# IN (4241) AND #%p_rel_group%# IN (68)");
        await client.GetActiveAsync(100, CancellationToken.None);
        handler.Requests[1].Filter.Should().Contain("NOT IN").And.Contain("4241");
        handler.Requests.Should().OnlyContain(r => r.Path == "/query");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"QueryResult\":{\"ErrorNo\":1,\"Items\":[]}}")]
    [InlineData("{\"QueryResult\":{\"Items\":null}}")]
    [InlineData("{\"QueryResult\":{\"Items\":[[{\"Key\":\"SET.id\",\"Value\":\"100\"}]]}}")]
    [InlineData("{\"QueryResult\":{\"Items\":[[[{\"Value\":\"100\"}],[{\"Value\":\"OR-100\"}],[{\"Value\":\"title\"}],[{\"Value\":\"description\"}],[{\"Value\":\"requester\"}]]]}}")]
    public async Task Discovery_MalformedOrPositionalOnly_RejectsEntireBatch(string response)
    {
        using var handler = new Handler(response);
        Func<Task> read = () => Client(handler).DiscoverAsync(CancellationToken.None);
        await read.Should().ThrowAsync<InvalidDataException>();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Discovery_ZeroResults_IsNotProofOfCompleteness()
    {
        using var handler = new Handler("{\"QueryResult\":{\"Items\":[]}}");
        InUseBatch result = await Client(handler).DiscoverAsync(CancellationToken.None);
        result.Records.Should().BeEmpty();
        result.Complete.Should().BeFalse();
    }

    [Theory]
    [InlineData(11, 1)]
    [InlineData(1, 70000)]
    public async Task Diagnostic_ExcessCardinalityOrSize_ReturnsNoReport(int count, int size)
    {
        string related = JsonSerializer.Serialize(new
        {
            QueryResult = new
            {
                Items = Enumerable.Range(0, count).Select(_ =>
            new[] { new { Key = "SET.synthetic", Value = new string('x', size) } }).ToArray()
            }
        });
        using var handler = new Handler("{\"QueryResult\":{\"Items\":[" + _row + "]}}", related);
        await FluentActions.Awaiting(() => Client(handler).DiagnoseAsync("100", CancellationToken.None)).Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Diagnostic_ReadOnlyFenceOff_MakesNoRequest()
    {
        using var handler = new Handler("{}");
        await FluentActions.Awaiting(() => Client(handler, false).DiagnoseAsync("100", CancellationToken.None)).Should().ThrowAsync<InvalidDataException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Diagnostic_DifferentRoot_PreventsTraversal()
    {
        using var handler = new Handler("{\"QueryResult\":{\"Items\":[" + _row + "]}}");
        await FluentActions.Awaiting(() => Client(handler).DiagnoseAsync("101", CancellationToken.None)).Should().ThrowAsync<InvalidDataException>();
        handler.Requests.Should().ContainSingle();
    }

    private static TuruncuHatOperationalRecordClient Client(Handler handler, bool readOnly = true)
    {
        ITuruncuHatSessionManager sessions = Substitute.For<ITuruncuHatSessionManager>();
        sessions.GetSessionAsync(Arg.Any<CancellationToken>()).Returns("synthetic-session");
        return new(new HttpClient(handler) { BaseAddress = new Uri("https://source.invalid/") }, sessions,
            Options.Create(new TuruncuHatOptions { RelatedGroupId = 68, ExcludedDccIds = [4241] }), Options.Create(new OperationalRecordsOptions { ReadOnlyIntegrationMode = readOnly }),
            new EnterpriseIntegrationHealthState(), new EnterpriseIntegrationTelemetry(), NullLogger<TuruncuHatOperationalRecordClient>.Instance);
    }
    private sealed class Handler(string body, string? related = null) : HttpMessageHandler
    {
        public List<(string Path, string Filter)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Requests.Add((request.RequestUri!.AbsolutePath, json.RootElement.GetProperty("req").GetProperty("Filters")[0].GetString()!));
            return new(HttpStatusCode.OK) { Content = new StringContent(Requests.Count == 2 && related is not null ? related : body) };
        }
    }
}
