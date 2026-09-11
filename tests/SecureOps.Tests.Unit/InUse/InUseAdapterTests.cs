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
        using var handler = new Handler("{\"QueryResult\":{\"Items\":[" + _row + "]}}", InUseServiceItemParserTests.Response(4));
        TuruncuHatOperationalRecordClient client = Client(handler);
        InUseBatch result = await client.DiscoverAsync(CancellationToken.None);
        result.Complete.Should().BeFalse();
        result.Records.Single().Requester.Value.Should().Be("Synthetic requester");
        result.Records.Single().ServiceOwner.Value.Should().BeNull();
        result.Records.Single().Servers.Should().HaveCount(4);
        result.Records.Single().ServiceItemsState.Should().Be("Observed");
        result.Records.Single().AffectedAssetsState.Should().Be("NotQueried");
        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Path.Should().Be("/query");
        handler.Requests[0].Filter.Should().Be("#%m_active%#='True' AND #%p_dcc%# IN (4241) AND #%p_rel_group%# IN (68)");
        await client.GetActiveAsync(100, CancellationToken.None);
        handler.Requests[2].Filter.Should().Contain("NOT IN").And.Contain("4241");
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

    [Theory]
    [InlineData(200, "Ambiguous")]
    [InlineData(403, "Forbidden")]
    [InlineData(500, "Failed")]
    public async Task Discovery_RelationshipFailure_IsExplicitWithoutInventedServers(int status, string state)
    {
        using var handler = new Handler("{\"QueryResult\":{\"Items\":[" + _row + "]}}", "{}", (HttpStatusCode)status);
        InUseBatch result = await Client(handler).DiscoverAsync(CancellationToken.None);
        result.Records.Single().ServiceItemsState.Should().Be(state);
        result.Records.Single().Servers.Should().BeEmpty();
        result.Records.Single().Creator.Should().BeNull();
        result.Records.Single().ProvisioningTeam.Value.Should().BeNull();
        handler.Requests.Should().OnlyContain(r => r.Path == "/query");
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

    [Theory]
    [InlineData("SourceId", "SET", "200")]
    [InlineData("OrCode", "KEY", "OR-200")]
    public async Task Diagnostic_ApprovedRfcContract_DeduplicatesExactRequestsWithoutActiveScope(string kind, string cellKind, string reference)
    {
        string related = RfcRows(reference);
        string target = """
            {"QueryResult":{"Items":[[{"Key":"SET.id","Value":"200"},{"Key":"SET.p_code","Value":"OR-200"},
            {"Key":"SET.m_active","Value":"False"},{"Key":"KEY.p_rel_requester","Value":"Synthetic request owner"},
            {"Key":"SET.p_rel_requester","Value":"800"},{"Key":"KEY.p_synthetic_reporter","Value":"Synthetic reporter"}]]}}
            """;
        using var handler = new Handler("{\"QueryResult\":{\"Items\":[" + _row + "]}}", related, referenced: target);
        var comparison = new List<JsonElement>();
        JsonElement result = await Client(handler).DiagnoseAsync("100", new Dictionary<string, string> { ["RFC Kaydı"] = "p_synthetic_rfc" },
            CancellationToken.None, new("p_synthetic_rfc", cellKind, kind), comparison.Add);
        JsonElement references = result.GetProperty("ReferencedRequests");
        references.GetProperty("DistinctLookups").GetInt32().Should().Be(1);
        references.GetProperty("Links").GetArrayLength().Should().Be(4);
        references.GetProperty("Links")[0].GetProperty("Evidence").GetProperty("State").GetString().Should().Be("ExactMatchNotBusinessOwnership");
        references.GetProperty("Links")[0].GetProperty("Reference").GetString().Should()
            .Be(references.GetProperty("Links")[3].GetProperty("Reference").GetString());
        result.ToString().Should().NotContain("Synthetic request owner").And.NotContain("Synthetic reporter").And.NotContain("OR-200");
        comparison.Should().HaveCount(2); // One related set and one deduplicated exact target.
        string local = JsonSerializer.Serialize(comparison);
        local.Should().Contain("Synthetic request owner").And.Contain("OR-200").And.NotContain("p_synthetic_reporter").And.NotContain("m_active");
        handler.Requests.Should().HaveCount(3).And.OnlyContain(r => r.Path == "/query");
        handler.Requests[2].Filter.Should().Be(kind == "SourceId" ? "#%id%#=200" : "#%p_code%#='OR-200'");
    }

    [Theory]
    [InlineData("{\"QueryResult\":{\"Items\":[]}}", 200, "NotFoundOrNotVisible")]
    [InlineData("{\"QueryResult\":{\"Items\":[[],[]]}}", 200, "AmbiguousMatch")]
    [InlineData("{\"QueryResult\":{\"Items\":[[{\"Key\":\"SET.id\",\"Value\":\"300\"}]]}}", 200, "IdentityMismatch")]
    [InlineData("{}", 200, "MalformedOrAmbiguous")]
    [InlineData("{}", 403, "Forbidden")]
    public async Task Diagnostic_RfcMissingAmbiguousOrDenied_NeverSelectsFirstOrRecurses(string target, int status, string expected)
    {
        using var handler = new Handler("{\"QueryResult\":{\"Items\":[" + _row + "]}}", RfcRows("200"),
            referenced: target, referencedStatus: (HttpStatusCode)status);
        JsonElement result = await Client(handler).DiagnoseAsync("100", new Dictionary<string, string> { ["RFC Kaydı"] = "p_synthetic_rfc" },
            CancellationToken.None, new("p_synthetic_rfc", "SET", "SourceId"));
        result.GetProperty("ReferencedRequests").GetProperty("Links")[0].GetProperty("Evidence").GetProperty("State").GetString().Should().Be(expected);
        handler.Requests.Should().HaveCount(status == 403 ? 4 : 3); // Existing session renewal permits one retry.
    }

    [Fact]
    public async Task Diagnostic_UnapprovedRfcContract_StopsBeforeAnyTransport()
    {
        using var handler = new Handler("{}");
        await FluentActions.Awaiting(() => Client(handler).DiagnoseAsync("100", new Dictionary<string, string>(), CancellationToken.None,
            new("p_unverified", "SET", "SourceId", "p_synthetic_reporter"))).Should().ThrowAsync<InvalidDataException>();
        handler.Requests.Should().BeEmpty();
    }

    private static string RfcRows(string reference) => JsonSerializer.Serialize(new
    {
        QueryResult = new
        {
            Items = Enumerable.Range(1, 4).Select(i => new[] {
            new { Key = "SET.(LCSIMS_ServiceInstance)m_rid.id", Value = (1000 + i).ToString() },
            new { Key = "SET.(LCSIMS_ServiceInstance)m_rid.c_virtual_pc_user", Value = "" },
            new { Key = "SET.(LCSIMS_ServiceInstance)m_rid.p_synthetic_rfc", Value = reference },
            new { Key = "KEY.(LCSIMS_ServiceInstance)m_rid.p_synthetic_rfc", Value = reference } })
        }
    });

    [Theory]
    [InlineData("Returned", "owner")]
    [InlineData("Empty", "")]
    [InlineData("Omitted", null)]
    public async Task Diagnostic_RequesterOnly_DoesNotRequireReporterOrVirtualPcUser(string expected, string? value)
    {
        var cells = new List<object> { new { Key = "SET.id", Value = "200" }, new { Key = "SET.p_code", Value = "OR-200" } };
        if (value is not null)
        { cells.Add(new { Key = "KEY.p_rel_requester", Value = value }); }
        using var handler = new Handler("{\"QueryResult\":{\"Items\":[" + _row + "]}}", RfcRows("200"),
            referenced: JsonSerializer.Serialize(new { QueryResult = new { Items = new[] { cells } } }));
        JsonElement result = await Client(handler).DiagnoseAsync("100", new Dictionary<string, string> { ["RFC Kaydı"] = "p_synthetic_rfc" },
            CancellationToken.None, new("p_synthetic_rfc", "SET", "SourceId"));
        JsonElement evidence = result.GetProperty("ReferencedRequests").GetProperty("Links")[0].GetProperty("Evidence");
        evidence.GetProperty("RequesterState").GetString().Should().Be(expected);
        evidence.GetProperty("ReporterState").GetString().Should().Be(expected);
        evidence.GetProperty("ReporterLabel").GetString().Should().Be("Bildiren");
        evidence.GetProperty("ReporterReferenceState").GetString().Should().Be("Omitted");
        handler.Selects[2].Should().Equal("id", "p_code", "p_rel_requester");
        handler.Requests.Should().HaveCount(3);
        handler.Requests[2].Filter.Should().Be("#%id%#=200");
    }

    [Theory]
    [InlineData("missing", 0)]
    [InlineData("duplicate", 0)]
    [InlineData("different", 2)]
    public async Task Diagnostic_RfcCells_PreserveMissingConflictingAndPerServerReferences(string mode, int lookups)
    {
        const string prefix = "SET.(LCSIMS_ServiceInstance)m_rid.";
        object[] rows = Enumerable.Range(0, 2).Select(i =>
        {
            var cells = new List<object> { new { Key = prefix + "id", Value = (1000 + i).ToString() } };
            if (mode != "missing")
            { cells.Add(new { Key = prefix + "p_synthetic_rfc", Value = (200 + i).ToString() }); }
            if (mode == "duplicate")
            { cells.Add(new { Key = prefix + "p_synthetic_rfc", Value = "300" }); }
            cells.Reverse();
            return (object)cells;
        }).ToArray();
        string related = JsonSerializer.Serialize(new { QueryResult = new { Items = rows } });
        using var handler = new Handler("{\"QueryResult\":{\"Items\":[" + _row + "]}}", related,
            referenced: "{\"QueryResult\":{\"Items\":[]}}");
        JsonElement result = await Client(handler).DiagnoseAsync("100", new Dictionary<string, string> { ["RFC Kaydı"] = "p_synthetic_rfc" },
            CancellationToken.None, new("p_synthetic_rfc", "SET", "SourceId"));
        result.GetProperty("ReferencedRequests").GetProperty("DistinctLookups").GetInt32().Should().Be(lookups);
        handler.Requests.Should().HaveCount(2 + lookups);
        if (mode == "different")
        {
            handler.Requests.Skip(2).Select(r => r.Filter).Should().Equal("#%id%#=200", "#%id%#=201");
        }
        else
        {
            result.GetProperty("ReferencedRequests").GetProperty("Links")[0].GetProperty("State").GetString()
                .Should().Be(mode == "missing" ? "MissingIdentityOrReference" : "AmbiguousCells");
        }
    }

    [Fact]
    public async Task Diagnostic_CandidatesWithoutHop_ReportsNullEmptyOmittedAndNumericShapes()
    {
        const string related = """
            {"QueryResult":{"Items":[[
            {"Key":"SET.(LCSIMS_ServiceInstance)m_rid.id","Value":1001},
            {"Key":"SET.(LCSIMS_ServiceInstance)m_rid.c_virtual_pc_user","Value":""},
            {"Key":"SET.(LCSIMS_ServiceInstance)m_rid.c_rfc_record","Value":null}],
            [{"Key":"SET.(LCSIMS_ServiceInstance)m_rid.id","Value":1002}]]}}
            """;
        using var handler = new Handler("{\"QueryResult\":{\"Items\":[" + _row + "]}}", related);
        var comparison = new List<JsonElement>();
        JsonElement result = await Client(handler).DiagnoseAsync("100", new Dictionary<string, string>
        { ["RFC Kaydı"] = "c_rfc_record", ["Virtual PC User"] = "c_virtual_pc_user" }, CancellationToken.None, privateComparison: comparison.Add);
        handler.Requests.Should().HaveCount(2);
        handler.Selects[1].Should().HaveCount(17).And.NotContain(s => s.Contains("_i_", StringComparison.Ordinal));
        result.GetProperty("ReferencedRequests").ValueKind.Should().Be(JsonValueKind.Null);
        JsonElement fields = result.GetProperty("CandidateFields");
        fields[0].GetProperty("Contract").GetString().Should().Be("CandidateNotApproved");
        fields[0].GetProperty("Rows")[0].GetProperty("Cells")[0].GetProperty("ValueState").GetString().Should().Be("Null");
        fields[0].GetProperty("Rows")[1].GetProperty("Cells").GetArrayLength().Should().Be(0);
        fields[1].GetProperty("Rows")[0].GetProperty("Cells")[0].GetProperty("ValueState").GetString().Should().Be("Empty");
        result.GetRawText().Should().NotContain("1001").And.NotContain("1002");
        comparison.Should().ContainSingle();
        JsonSerializer.Serialize(comparison).Should().Contain("1001").And.NotContain("p_rel_requester");
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("SET", "")]
    public async Task Diagnostic_UnfilledRepresentationTemplate_MakesNoRequest(string cell, string kind)
    {
        using var handler = new Handler("{}");
        await FluentActions.Awaiting(() => Client(handler).DiagnoseAsync("100",
            new Dictionary<string, string> { ["RFC Kaydı"] = "c_rfc_record" }, CancellationToken.None,
            new("c_rfc_record", cell, kind))).Should().ThrowAsync<InvalidDataException>();
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("[\"private-A\",\"private-B\"]", "Array")]
    [InlineData("{\"private-A\":1,\"private-B\":2}", "Object")]
    public async Task Diagnostic_CompoundCandidate_ReportsCardinalityWithoutFlattening(string value, string type)
    {
        string related = "{\"QueryResult\":{\"Items\":[[{\"Key\":\"SET.(LCSIMS_ServiceInstance)m_rid.c_rfc_record\",\"Value\":" + value + "}]]}}";
        using var handler = new Handler("{\"QueryResult\":{\"Items\":[" + _row + "]}}", related);
        var comparison = new List<JsonElement>();
        JsonElement result = await Client(handler).DiagnoseAsync("100", new Dictionary<string, string>
        { ["RFC Kaydı"] = "c_rfc_record" }, CancellationToken.None, privateComparison: comparison.Add);
        JsonElement cell = result.GetProperty("ServiceItems")[0][0];
        cell.GetProperty("Type").GetString().Should().Be(type);
        cell.GetProperty("Cardinality").GetInt32().Should().Be(2);
        result.GetRawText().Should().NotContain("private-A");
        JsonSerializer.Serialize(comparison).Should().NotContain("private-A");
        handler.Requests.Should().HaveCount(2);
    }

    private static TuruncuHatOperationalRecordClient Client(Handler handler, bool readOnly = true)
    {
        ITuruncuHatSessionManager sessions = Substitute.For<ITuruncuHatSessionManager>();
        sessions.GetSessionAsync(Arg.Any<CancellationToken>()).Returns("synthetic-session");
        return new(new HttpClient(handler) { BaseAddress = new Uri("https://source.invalid/") }, sessions,
            Options.Create(new TuruncuHatOptions { RelatedGroupId = 68, ExcludedDccIds = [4241] }), Options.Create(new OperationalRecordsOptions { ReadOnlyIntegrationMode = readOnly }),
            new EnterpriseIntegrationHealthState(), new EnterpriseIntegrationTelemetry(), NullLogger<TuruncuHatOperationalRecordClient>.Instance);
    }
    private sealed class Handler(string body, string? related = null, HttpStatusCode relatedStatus = HttpStatusCode.OK,
        string? referenced = null, HttpStatusCode referencedStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<(string Path, string Filter)> Requests { get; } = [];
        public List<string[]> Selects { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Selects.Add(json.RootElement.GetProperty("req").GetProperty("Selects").EnumerateArray().Select(s => s.GetString()!).ToArray());
            Requests.Add((request.RequestUri!.AbsolutePath, json.RootElement.GetProperty("req").GetProperty("Filters")[0].GetString()!));
            return new(Requests.Count > 2 && referenced is not null ? referencedStatus : Requests.Count > 1 ? relatedStatus : HttpStatusCode.OK)
            { Content = new StringContent(Requests.Count > 2 && referenced is not null ? referenced : Requests.Count == 2 && related is not null ? related : body) };
        }
    }
}
