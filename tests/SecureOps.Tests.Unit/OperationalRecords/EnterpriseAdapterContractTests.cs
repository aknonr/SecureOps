using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Tests.Unit.OperationalRecords;

public sealed class EnterpriseAdapterContractTests
{
    [Fact]
    public async Task SessionManager_UsesReviewedLoginEnvelope_AndCachesSession()
    {
        ScriptedHandler handler = new(Response(HttpStatusCode.OK, "{\"LoginResult\":\"ok|sanitized-session\"}"));
        TuruncuHatSessionManager manager = SessionManager(handler);

        string first = await manager.GetSessionAsync(CancellationToken.None);
        string second = await manager.GetSessionAsync(CancellationToken.None);

        first.Should().Be("ok|sanitized-session");
        second.Should().Be(first);
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Path.Should().Be("/login");
        using var body = JsonDocument.Parse(handler.Requests[0].Body);
        body.RootElement.GetProperty("req").GetProperty("TenantId").GetInt32().Should().Be(218);
        handler.Requests[0].Authorization.Should().Be("Sanitized runtime value");
    }

    [Fact]
    public async Task SessionManager_RejectsOneCharacterSessionSegment()
    {
        ScriptedHandler handler = new(Response(HttpStatusCode.OK, "{\"LoginResult\":\"ok|x\"}"));
        TuruncuHatSessionManager manager = SessionManager(handler);

        Func<Task> act = async () => await manager.GetSessionAsync(CancellationToken.None);

        ExternalIntegrationException exception = (await act.Should().ThrowAsync<ExternalIntegrationException>()).Which;
        exception.ErrorCode.Should().Be(OperationalErrorCodes.OperationalSourceAuthenticationFailed);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, OperationalErrorCodes.OperationalSourceAuthenticationFailed, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, OperationalErrorCodes.OperationalSourceUnavailable, true)]
    public async Task SessionManager_ClassifiesRejectedLoginSafely(HttpStatusCode status, string code, bool retryable)
    {
        ScriptedHandler handler = new(Response(status, "{}"));
        TuruncuHatSessionManager manager = SessionManager(handler);

        Func<Task> act = async () => await manager.GetSessionAsync(CancellationToken.None);

        ExternalIntegrationException exception = (await act.Should().ThrowAsync<ExternalIntegrationException>()).Which;
        exception.ErrorCode.Should().Be(code);
        exception.Retryable.Should().Be(retryable);
    }

    [Fact]
    public async Task SourceQuery_ExpiredSession_PerformsOneControlledRelogin()
    {
        ScriptedHandler handler = new(
            Response(HttpStatusCode.OK, "{\"LoginResult\":\"ok|session-one\"}"),
            Response(HttpStatusCode.Unauthorized, "{}"),
            Response(HttpStatusCode.OK, "{\"LoginResult\":\"ok|session-two\"}"),
            Response(HttpStatusCode.OK, "{\"QueryResult\":{\"Items\":[]}}"));
        HttpClient httpClient = Client(handler, "https://source.invalid/");
        TuruncuHatOptions options = TuruncuOptions();
        EnterpriseIntegrationHealthState health = new();
        EnterpriseIntegrationTelemetry telemetry = new();
        TuruncuHatSessionManager manager = new(
            httpClient, Options.Create(options), TimeProvider.System, health, telemetry,
            NullLogger<TuruncuHatSessionManager>.Instance);
        TuruncuHatOperationalRecordClient client = new(
            httpClient, manager, Options.Create(options), Options.Create(new OperationalRecordsOptions()), health, telemetry,
            NullLogger<TuruncuHatOperationalRecordClient>.Instance);

        IReadOnlyList<OperationalRecordSourceItem> records = await client.GetActiveAsync(10, CancellationToken.None);

        records.Should().BeEmpty();
        handler.Requests.Select(request => request.Path).Should().Equal("/login", "/query", "/login", "/query");
    }

    [Fact]
    public async Task SourceClient_ParsesReviewedProjection_AndHtmlDecodesText()
    {
        const string response = """
            {"QueryResult":{"ErrorDescription":"","ErrorDetails":"","ErrorNo":0,"TenantId":218,
              "Items":[[{"Key":"SET.id","Value":"1001"},
                         {"Key":"SET.p_code","Value":"OR-100"},
                         {"Key":"SET.p_name","Value":"Short &amp; safe"},
                         {"Key":"SET.p_description","Value":"Detail &lt;encoded&gt;"},
                         {"Key":"KEY.p_rel_requester","Value":"Exact Requester"},
                         {"Key":"SET.p_rel_requester","Value":"12345"},
                         {"Key":"num","Value":"1"}]],
              "MaxPages":1,"PageNo":1,"RecordCount":1}}
            """;
        ScriptedHandler handler = new(Response(HttpStatusCode.OK, response));
        TuruncuHatOperationalRecordClient client = SourceClient(handler);

        IReadOnlyList<OperationalRecordSourceItem> records = await client.GetActiveAsync(10, CancellationToken.None);

        records.Should().ContainSingle();
        records[0].Title.Should().Be("Short & safe");
        records[0].Description.Should().Be("Detail <encoded>");
        records[0].CreatedAt.Should().BeNull();
        handler.Requests[0].Body.Should().Contain("SMSS_oRFF").And.Contain("p_rel_group");
    }

    [Fact]
    public async Task SourceClient_MapsExactCorporateRelationalProjectionWithoutAmbiguity()
    {
        const string response = """
            {"QueryResult":{"Items":[[
              {"Key":"SET.id","Value":"1683742"},
              {"Key":"SET.p_code","Value":"OR-00668218"},
              {"Key":"SET.p_name","Value":"safe title"},
              {"Key":"SET.p_description","Value":"safe description"},
              {"Key":"KEY.p_rel_requester","Value":"SAFE USER DISPLAY NAME"},
              {"Key":"SET.p_rel_requester","Value":"12345"},
              {"Key":"num","Value":"1"}
            ]]}}
            """;
        CapturingLogger<TuruncuHatOperationalRecordClient> logger = new();

        IReadOnlyList<OperationalRecordSourceItem> records = await SourceClient(
            new ScriptedHandler(Response(HttpStatusCode.OK, response)),
            logger: logger).GetActiveAsync(10, CancellationToken.None);

        OperationalRecordSourceItem record = records.Should().ContainSingle().Which;
        record.SourceRecordId.Should().Be("1683742");
        record.OrCode.Should().Be("OR-00668218");
        record.Title.Should().Be("safe title");
        record.Description.Should().Be("safe description");
        record.Requester.Should().Be("SAFE USER DISPLAY NAME");
        string.Join(' ', logger.Messages).Should()
            .Contain("Records: 1")
            .And.Contain("MalformedOrAmbiguous: 0");
    }

    [Fact]
    public async Task SourceClient_ParsesFourDirectCorporateKeyValueRecordsWithoutMalformedRows()
    {
        const string response = """
            {"QueryResult":{"ErrorDescription":null,"ErrorDetails":null,"ErrorNo":0,"TenantId":0,"MaxPages":0,"PageNO":0,"RecordCount":4,"Items":[
              [{"Key":"SET.id","Value":"1683742"},{"Key":"SET.p_code","Value":"OR-00668218"},{"Key":"SET.p_name","Value":"safe title 1"},{"Key":"SET.p_description","Value":"safe description 1"},{"Key":"KEY.p_rel_requester","Value":"safe requester 1"},{"Key":"SET.p_rel_requester","Value":"12345"},{"Key":"num","Value":"1"}],
              [{"Key":"SET.id","Value":"1682619"},{"Key":"SET.p_code","Value":"OR-00667092"},{"Key":"SET.p_name","Value":"safe title 2"},{"Key":"SET.p_description","Value":"safe description 2"},{"Key":"KEY.p_rel_requester","Value":"safe requester 2"},{"Key":"SET.p_rel_requester","Value":"23456"},{"Key":"num","Value":"2"}],
              [{"Key":"SET.id","Value":"1676990"},{"Key":"SET.p_code","Value":"OR-00661461"},{"Key":"SET.p_name","Value":"safe title 3"},{"Key":"SET.p_description","Value":"safe description 3"},{"Key":"KEY.p_rel_requester","Value":"safe requester 3"},{"Key":"SET.p_rel_requester","Value":"34567"},{"Key":"num","Value":"3"}],
              [{"Key":"SET.id","Value":"1668538"},{"Key":"SET.p_code","Value":"OR-00653003"},{"Key":"SET.p_name","Value":"safe title 4"},{"Key":"SET.p_description","Value":"safe description 4"},{"Key":"KEY.p_rel_requester","Value":"safe requester 4"},{"Key":"SET.p_rel_requester","Value":"45678"},{"Key":"num","Value":"4"}]
            ]}}
            """;
        CapturingLogger<TuruncuHatOperationalRecordClient> logger = new();

        IReadOnlyList<OperationalRecordSourceItem> records = await SourceClient(
            new ScriptedHandler(Response(HttpStatusCode.OK, response)),
            logger: logger).GetActiveAsync(10, CancellationToken.None);

        records.Should().HaveCount(4);
        records.Select(record => record.SourceRecordId).Should().Equal("1683742", "1682619", "1676990", "1668538");
        records.Select(record => record.OrCode).Should().Equal("OR-00668218", "OR-00667092", "OR-00661461", "OR-00653003");
        records[0].Title.Should().Be("safe title 1");
        records[0].Description.Should().Be("safe description 1");
        records[0].Requester.Should().Be("safe requester 1");
        string.Join(' ', logger.Messages).Should()
            .Contain("Records: 4")
            .And.Contain("MalformedOrAmbiguous: 0")
            .And.NotContain("safe description")
            .And.NotContain("safe requester");
    }

    [Fact]
    public async Task SourceClient_RejectsConflictingIncompleteOrUnboundedDirectProjections()
    {
        (string Name, string Record)[] rejected =
        [
            ("unprefixed scalar keys", """[{"Key":"id","Value":"1001"},{"Key":"p_code","Value":"OR-100"},{"Key":"p_name","Value":"Title"},{"Key":"p_description","Value":"Description"}]"""),
            ("duplicate required key", """[{"Key":"SET.id","Value":"1001"},{"Key":"SET.id","Value":"1001"},{"Key":"SET.p_code","Value":"OR-100"},{"Key":"SET.p_name","Value":"Title"},{"Key":"SET.p_description","Value":"Description"}]"""),
            ("conflicting duplicate metadata", """[{"Key":"SET.id","Value":"1001"},{"Key":"SET.p_code","Value":"OR-100"},{"Key":"SET.p_name","Value":"Title"},{"Key":"SET.p_description","Value":"Description"},{"Key":"unknown","Value":"one"},{"Key":"unknown","Value":"two"}]"""),
            ("missing required field", """[{"Key":"SET.id","Value":"1001"},{"Key":"SET.p_code","Value":"OR-100"},{"Key":"SET.p_name","Value":"Title"},{"Key":"KEY.p_rel_requester","Value":"Requester"}]"""),
            ("null required value", """[{"Key":"SET.id","Value":"1001"},{"Key":"SET.p_code","Value":"OR-100"},{"Key":"SET.p_name","Value":"Title"},{"Key":"SET.p_description","Value":null}]"""),
            ("mixed direct and nested shapes", """[{"Key":"SET.id","Value":"1001"},[{"Key":"SET.p_code","Value":"OR-100"}],{"Key":"SET.p_name","Value":"Title"},{"Key":"SET.p_description","Value":"Description"}]""")
        ];

        foreach ((string name, string record) in rejected)
        {
            CapturingLogger<TuruncuHatOperationalRecordClient> logger = new();
            string response = "{\"QueryResult\":{\"Items\":[" + record + "]}}";

            IReadOnlyList<OperationalRecordSourceItem> records = await SourceClient(
                new ScriptedHandler(Response(HttpStatusCode.OK, response)),
                logger: logger).GetActiveAsync(10, CancellationToken.None);

            records.Should().BeEmpty(name);
            string.Join(' ', logger.Messages).Should().Contain("MalformedOrAmbiguous: 1", name);
        }
    }

    [Fact]
    public async Task SourceClient_AllowsEmptyOptionalRequesterInDirectProjection()
    {
        const string response = """
            {"QueryResult":{"Items":[[
              {"Key":"SET.id","Value":"1001"},
              {"Key":"SET.p_code","Value":"OR-100"},
              {"Key":"SET.p_name","Value":"Title"},
              {"Key":"SET.p_description","Value":"Description"},
              {"Key":"KEY.p_rel_requester","Value":""},
              {"Key":"SET.p_rel_requester","Value":"12345"},
              {"Key":"num","Value":"1"}
            ]]}}
            """;
        CapturingLogger<TuruncuHatOperationalRecordClient> logger = new();

        IReadOnlyList<OperationalRecordSourceItem> records = await SourceClient(
            new ScriptedHandler(Response(HttpStatusCode.OK, response)),
            logger: logger).GetActiveAsync(10, CancellationToken.None);

        records.Should().ContainSingle().Which.Requester.Should().BeNull();
        string.Join(' ', logger.Messages).Should().Contain("MalformedOrAmbiguous: 0");
    }

    [Fact]
    public async Task SourceClient_DoesNotUseInternalRequesterWhenDisplayCellIsAbsent()
    {
        const string response = """
            {"QueryResult":{"Items":[[
              {"Key":"SET.id","Value":"1001"},
              {"Key":"SET.p_code","Value":"OR-100"},
              {"Key":"SET.p_name","Value":"Title"},
              {"Key":"SET.p_description","Value":"Description"},
              {"Key":"SET.p_rel_requester","Value":"12345"},
              {"Key":"num","Value":"1"}
            ]]}}
            """;

        IReadOnlyList<OperationalRecordSourceItem> records = await SourceClient(
            new ScriptedHandler(Response(HttpStatusCode.OK, response))).GetActiveAsync(10, CancellationToken.None);

        records.Should().ContainSingle().Which.Requester.Should().BeNull();
    }

    [Fact]
    public async Task SourceClient_IgnoresBoundedNonConflictingUnknownMetadataIndependentOfPosition()
    {
        const string response = """
            {"QueryResult":{"Items":[[
              {"Key":"unknown","Value":"bounded metadata"},
              {"Key":"SET.p_description","Value":"Description"},
              {"Key":"SET.p_name","Value":"Title"},
              {"Key":"SET.p_code","Value":"OR-100"},
              {"Key":"SET.id","Value":"1001"},
              {"Key":"unknown","Value":"bounded metadata"}
            ]]}}
            """;

        IReadOnlyList<OperationalRecordSourceItem> records = await SourceClient(
            new ScriptedHandler(Response(HttpStatusCode.OK, response))).GetActiveAsync(10, CancellationToken.None);

        records.Should().ContainSingle().Which.OrCode.Should().Be("OR-100");
    }

    [Fact]
    public async Task SourceClient_RejectsSourceProjectionAboveCellBound()
    {
        string extras = string.Join(',', Enumerable.Range(0, 29)
            .Select(index => $"{{\"Key\":\"unknown-{index}\",\"Value\":\"metadata\"}}"));
        string response = "{\"QueryResult\":{\"Items\":[["
            + "{\"Key\":\"SET.id\",\"Value\":\"1001\"},"
            + "{\"Key\":\"SET.p_code\",\"Value\":\"OR-100\"},"
            + "{\"Key\":\"SET.p_name\",\"Value\":\"Title\"},"
            + "{\"Key\":\"SET.p_description\",\"Value\":\"Description\"},"
            + extras + "]]}}";
        CapturingLogger<TuruncuHatOperationalRecordClient> logger = new();

        IReadOnlyList<OperationalRecordSourceItem> records = await SourceClient(
            new ScriptedHandler(Response(HttpStatusCode.OK, response)),
            logger: logger).GetActiveAsync(10, CancellationToken.None);

        records.Should().BeEmpty();
        string.Join(' ', logger.Messages).Should().Contain("MalformedOrAmbiguous: 1");
    }

    [Fact]
    public async Task SourceClient_UsesExactKnownGoodCorporateQueryContract()
    {
        const string legacyContract = "{\"req\":{\"BaseObject\":\"SMSS_oRFF\",\"Filters\":[\"#%m_active%#='True' AND #%p_dcc%# NOT IN (4241) AND #%p_rel_group%# IN (68)\"],\"Selects\":[\"id\",\"p_code\",\"p_name\",\"p_description\",\"p_rel_requester\"],\"SessionID\":\"<session>\",\"TenantId\":218}}";
        ScriptedHandler handler = new(Response(HttpStatusCode.OK, "{\"QueryResult\":{\"Items\":[]}}"));

        _ = await SourceClient(handler).GetActiveAsync(10, CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        CapturedRequest request = handler.Requests[0];
        request.Path.Should().Be("/query");
        request.ContentType.Should().StartWith("application/json");
        var actualContract = JsonNode.Parse(request.Body);
        var expectedContract = JsonNode.Parse(
            legacyContract.Replace("<session>", "sanitized-session", StringComparison.Ordinal));
        JsonNode.DeepEquals(actualContract, expectedContract).Should().BeTrue();
        using var body = JsonDocument.Parse(request.Body);
        body.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal("req");
        JsonElement req = body.RootElement.GetProperty("req");
        req.EnumerateObject().Select(property => property.Name).Should().Equal(
            "BaseObject", "Filters", "Selects", "SessionID", "TenantId");
        req.GetProperty("BaseObject").GetString().Should().Be("SMSS_oRFF");
        req.GetProperty("Filters").EnumerateArray().Select(value => value.GetString()).Should().Equal(
            "#%m_active%#='True' AND #%p_dcc%# NOT IN (4241) AND #%p_rel_group%# IN (68)");
        req.GetProperty("Selects").EnumerateArray().Select(value => value.GetString()).Should().Equal(
            "id", "p_code", "p_name", "p_description", "p_rel_requester");
        req.GetProperty("SessionID").GetString().Should().Be("sanitized-session");
        req.GetProperty("TenantId").GetInt32().Should().Be(218);
    }

    [Fact]
    public async Task SourceClient_ParsesRealCorporateFourItemNestedProjection_WithEmptyErrors()
    {
        const string response = """
            {"QueryResult":{"ErrorDescription":null,"ErrorDetails":null,"ErrorNo":0,"TenantId":0,"MaxPages":0,"PageNO":0,"RecordCount":4,"Items":[
              [[{"Value":"1001"}],[{"Value":"OR-100"}],[{"Value":"One"}],[{"Value":"Description 1"}],[{"Value":"Requester 1"}]],
              [[{"Value":"1002"}],[{"Value":"OR-200"}],[{"Value":"Two"}],[{"Value":"Description 2"}],[{"Value":"Requester 2"}]],
              [[{"Value":"1003"}],[{"Value":"OR-300"}],[{"Value":"Three"}],[{"Value":"Description 3"}],[{"Value":"Requester 3"}]],
              [[{"Value":"1004"}],[{"Value":"OR-400"}],[{"Value":"Four"}],[{"Value":"Description 4"}],[{"Value":"Requester 4"}]]
            ]}}
            """;

        IReadOnlyList<OperationalRecordSourceItem> records = await SourceClient(
            new ScriptedHandler(Response(HttpStatusCode.OK, response))).GetActiveAsync(10, CancellationToken.None);

        records.Should().HaveCount(4);
        records.Select(record => record.SourceRecordId).Should().Equal("1001", "1002", "1003", "1004");
    }

    [Fact]
    public async Task SourceClient_PreservesLegacyKeylessNestedProjection()
    {
        const string response = """
            {"QueryResult":{"Items":[[
              [{"Value":"1001"}],
              [{"Value":"OR-100"}],
              [{"Value":"Title"}],
              [{"Value":"Description"}],
              [{"Value":"Requester"}]
            ]]}}
            """;

        IReadOnlyList<OperationalRecordSourceItem> records = await SourceClient(
            new ScriptedHandler(Response(HttpStatusCode.OK, response))).GetActiveAsync(10, CancellationToken.None);

        records.Should().ContainSingle().Which.OrCode.Should().Be("OR-100");
    }

    [Fact]
    public async Task SessionManager_PreservesCompleteLoginResultInQuerySessionId()
    {
        string loginResult = "abc|" + new string('s', 64);
        ScriptedHandler handler = new(
            Response(HttpStatusCode.OK, $"{{\"LoginResult\":\"{loginResult}\"}}"),
            Response(HttpStatusCode.OK, "{\"QueryResult\":{\"Items\":[]}}"));
        TuruncuHatOptions options = TuruncuOptions();
        HttpClient httpClient = Client(handler, "https://source.invalid/");
        EnterpriseIntegrationHealthState health = new();
        EnterpriseIntegrationTelemetry telemetry = new();
        TuruncuHatSessionManager manager = new(
            httpClient, Options.Create(options), TimeProvider.System, health, telemetry,
            NullLogger<TuruncuHatSessionManager>.Instance);
        TuruncuHatOperationalRecordClient client = new(
            httpClient, manager, Options.Create(options), Options.Create(new OperationalRecordsOptions()), health, telemetry,
            NullLogger<TuruncuHatOperationalRecordClient>.Instance);

        _ = await client.GetActiveAsync(10, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Requests[1].Body);
        body.RootElement.GetProperty("req").GetProperty("SessionID").GetString().Should().Be(loginResult);
    }

    [Theory]
    [InlineData("{\"QueryResult\":{\"ErrorDescription\":null,\"ErrorDetails\":null,\"Items\":[]}}")]
    [InlineData("{\"QueryResult\":{\"ErrorDescription\":\"\",\"ErrorDetails\":\"\",\"Items\":[]}}")]
    [InlineData("{\"QueryResult\":{\"ErrorNo\":0,\"Items\":[]}}")]
    public async Task SourceClient_AcceptsIndependentlyOptionalEmptyOrNullErrorMetadata(string response)
    {
        IReadOnlyList<OperationalRecordSourceItem> records = await SourceClient(
            new ScriptedHandler(Response(HttpStatusCode.OK, response))).GetActiveAsync(10, CancellationToken.None);

        records.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{\"QueryResult\":{\"ErrorDescription\":\"Rejected\",\"Items\":[]}}")]
    [InlineData("{\"QueryResult\":{\"ErrorDetails\":\"Rejected\",\"Items\":[]}}")]
    [InlineData("{\"QueryResult\":{\"ErrorNo\":1,\"Items\":[]}}")]
    public async Task SourceClient_RejectsCorporateQueryResultErrors(string response)
    {
        Func<Task> act = async () => await SourceClient(
            new ScriptedHandler(Response(HttpStatusCode.OK, response))).GetActiveAsync(10, CancellationToken.None);

        ExternalIntegrationException exception = (await act.Should().ThrowAsync<ExternalIntegrationException>()).Which;
        exception.ErrorCode.Should().Be(OperationalErrorCodes.OperationalRecordQueryFailed);
    }

    [Fact]
    public async Task SourceClient_MissingItemsIsRejected()
    {
        Func<Task> act = async () => await SourceClient(
            new ScriptedHandler(Response(HttpStatusCode.OK, "{\"QueryResult\":{\"ErrorNo\":0}}")))
            .GetActiveAsync(10, CancellationToken.None);

        ExternalIntegrationException exception = (await act.Should().ThrowAsync<ExternalIntegrationException>()).Which;
        exception.ErrorCode.Should().Be(OperationalErrorCodes.OperationalRecordQueryFailed);
    }

    [Fact]
    public async Task SourceClient_FreshnessReadAddsExactValidatedSourceIdToServerFilter()
    {
        const string response = "{\"QueryResult\":{\"Items\":[]}}";
        ScriptedHandler handler = new(Response(HttpStatusCode.OK, response));
        TuruncuHatOperationalRecordClient client = SourceClient(handler);

        OperationalRecordSourceItem? record = await client.GetByIdAsync("1001", CancellationToken.None);

        record.Should().BeNull();
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Body.Should().Contain("#%id%#=1001");
    }

    [Fact]
    public async Task SourceClient_NormalActiveReadDoesNotAddExactSourceIdFilter()
    {
        ScriptedHandler handler = new(Response(HttpStatusCode.OK, "{\"QueryResult\":{\"Items\":[]}}"));

        _ = await SourceClient(handler).GetActiveAsync(10, CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Body.Should().NotContain("#%id%#");
    }

    [Fact]
    public async Task SourceClient_InvalidFreshnessIdentityFailsClosedWithoutNetworkDispatch()
    {
        ScriptedHandler handler = new();
        TuruncuHatOperationalRecordClient client = SourceClient(handler);

        OperationalRecordSourceItem? record = await client.GetByIdAsync("1001 OR 1=1", CancellationToken.None);

        record.Should().BeNull();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SourceClient_ExcludesEveryDuplicateIdentifierAsAmbiguous()
    {
        const string response = """
            {"QueryResult":{"Items":[
              [[{"Value":"1001"}],[{"Value":"OR-100"}],[{"Value":"One"}],[{"Value":"Description"}],[]],
              [[{"Value":"1001"}],[{"Value":"OR-200"}],[{"Value":"Two"}],[{"Value":"Description"}],[]]
            ]}}
            """;
        ScriptedHandler handler = new(Response(HttpStatusCode.OK, response));
        TuruncuHatOperationalRecordClient client = SourceClient(handler);

        IReadOnlyList<OperationalRecordSourceItem> records = await client.GetActiveAsync(10, CancellationToken.None);

        records.Should().BeEmpty();
    }

    [Fact]
    public async Task SourceClient_SkipsMalformedItemWithoutLosingValidItem()
    {
        const string response = """
            {"QueryResult":{"Items":[
              [[{"Value":"1001"}],[{"Value":"OR-100"}],[{"Value":"Valid"}],[{"Value":"Description"}],[]],
              [[{"Value":"missing-fields"}]]
            ]}}
            """;
        ScriptedHandler handler = new(Response(HttpStatusCode.OK, response));

        IReadOnlyList<OperationalRecordSourceItem> records = await SourceClient(handler).GetActiveAsync(10, CancellationToken.None);

        records.Should().ContainSingle().Which.OrCode.Should().Be("OR-100");
    }

    [Fact]
    public async Task SourceClient_KeyedProjection_RejectsUnprefixedOrDuplicateRequiredKeys()
    {
        const string response = """
            {"QueryResult":{"Items":[
              [[{"Key":"id","Value":"1001"}],[{"Key":"p_code","Value":"OR-100"}],[{"Key":"p_name","Value":"Title"}],[{"Key":"p_description","Value":"Description"}],[{"Key":"unexpected","Value":"Requester"}]],
              [[{"Key":"SET.id","Value":"1002"}],[{"Key":"SET.p_code","Value":"OR-200"}],[{"Key":"SET.p_name","Value":"Title"}],[{"Key":"SET.p_name","Value":"Title"}],[{"Key":"SET.p_description","Value":"Description"}]]
            ]}}
            """;

        IReadOnlyList<OperationalRecordSourceItem> records = await SourceClient(
            new ScriptedHandler(Response(HttpStatusCode.OK, response))).GetActiveAsync(10, CancellationToken.None);

        records.Should().BeEmpty();
    }

    [Fact]
    public async Task SourceClient_AppliesMaximumCountAfterValidatingCorporateResponse()
    {
        const string response = """
            {"QueryResult":{"Items":[
              [[{"Value":"1001"}],[{"Value":"OR-100"}],[{"Value":"One"}],[{"Value":"Description"}],[]],
              [[{"Value":"1002"}],[{"Value":"OR-200"}],[{"Value":"Two"}],[{"Value":"Description"}],[]],
              [[{"Value":"1003"}],[{"Value":"OR-300"}],[{"Value":"Three"}],[{"Value":"Description"}],[]]
            ]}}
            """;

        ScriptedHandler handler = new(Response(HttpStatusCode.OK, response));
        IReadOnlyList<OperationalRecordSourceItem> records = await SourceClient(handler).GetActiveAsync(2, CancellationToken.None);

        records.Select(record => record.SourceRecordId).Should().Equal("1001", "1002");
        using var request = JsonDocument.Parse(handler.Requests[0].Body);
        request.RootElement.GetProperty("req").EnumerateObject().Select(property => property.Name)
            .Should().NotContain(name => name.Contains("Page", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Count", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Limit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SourceClient_DiagnosticsNeverContainCredentialsOrSessionIdentifier()
    {
        CapturingLogger<TuruncuHatOperationalRecordClient> logger = new();
        ScriptedHandler handler = new(Response(HttpStatusCode.BadRequest, "{}"));

        Func<Task> act = async () => await SourceClient(handler, logger: logger)
            .GetActiveAsync(10, CancellationToken.None);

        await act.Should().ThrowAsync<ExternalIntegrationException>();
        string messages = string.Join(' ', logger.Messages);
        messages.Should().Contain("HTTP failure");
        messages.Should().NotContain("Sanitized runtime value")
            .And.NotContain("sanitized-user")
            .And.NotContain("sanitized-secret")
            .And.NotContain("sanitized-session");
    }

    [Fact]
    public async Task SourceClient_TestContractDiagnostic_LogsBoundedHttpFailureMetadata()
    {
        const string response = "{}";
        CapturingLogger<TuruncuHatOperationalRecordClient> logger = new();

        Func<Task> act = async () => await SourceClient(
            new ScriptedHandler(Response(HttpStatusCode.BadRequest, response)),
            logger: logger,
            diagnosticContractLogging: true).GetActiveAsync(10, CancellationToken.None);

        await act.Should().ThrowAsync<ExternalIntegrationException>();
        string messages = string.Join(' ', logger.Messages);
        messages.Should().Contain("query contract inbound")
            .And.Contain("HttpStatus: 400")
            .And.Contain($"ResponseByteLength: {Encoding.UTF8.GetByteCount(response)}")
            .And.Contain("QueryResultExists: False")
            .And.NotContain("sanitized-session")
            .And.NotContain("Sanitized runtime value")
            .And.NotContain("sanitized-secret");
    }

    [Fact]
    public async Task SourceClient_ApplicationErrorDiagnosticsContainOnlySanitizedMetadata()
    {
        CapturingLogger<TuruncuHatOperationalRecordClient> logger = new();
        const string response = "{\"QueryResult\":{\"ErrorNo\":1,\"ErrorDescription\":\"secret remote text\",\"ErrorDetails\":\"secret details\",\"TenantId\":0,\"MaxPages\":0,\"PageNO\":0,\"RecordCount\":4,\"Items\":[]}}";

        Func<Task> act = async () => await SourceClient(
            new ScriptedHandler(Response(HttpStatusCode.OK, response)), logger: logger)
            .GetActiveAsync(10, CancellationToken.None);

        await act.Should().ThrowAsync<ExternalIntegrationException>();
        string messages = string.Join(' ', logger.Messages);
        messages.Should().Contain("FailureConditions: ErrorNo,ErrorDescription,ErrorDetails")
            .And.Contain("ErrorNo: 1")
            .And.Contain("HasErrorDescription: True")
            .And.Contain("HasErrorDetails: True")
            .And.Contain("HasItems: True")
            .And.Contain("TenantMetadata: 0")
            .And.Contain("PageNo: 0")
            .And.Contain("MaxPages: 0")
            .And.Contain("RecordCount: 4")
            .And.NotContain("secret remote text")
            .And.NotContain("secret details");
    }

    [Fact]
    public async Task SourceClient_TestContractDiagnostic_LogsExactSafeRequestAndResponseMetadata()
    {
        string loginResult = "abc|" + new string('s', 64);
        const string response = "{\"QueryResult\":{\"ErrorDescription\":null,\"ErrorDetails\":null,\"ErrorNo\":0,\"TenantId\":0,\"MaxPages\":0,\"PageNO\":0,\"RecordCount\":1,\"Items\":[[[{\"Value\":\"1001\"}],[{\"Value\":\"OR-100\"}],[{\"Value\":\"One\"}],[{\"Value\":\"private description\"}],[{\"Value\":\"Requester\"}]]]}}";
        CapturingLogger<TuruncuHatOperationalRecordClient> logger = new();
        ScriptedHandler handler = new(Response(HttpStatusCode.OK, response));

        IReadOnlyList<OperationalRecordSourceItem> records = await SourceClient(
            handler,
            logger: logger,
            diagnosticContractLogging: true,
            session: loginResult).GetActiveAsync(10, CancellationToken.None);

        records.Should().ContainSingle();
        string messages = string.Join(' ', logger.Messages);
        messages.Should().Contain("query contract outbound")
            .And.Contain("EndpointPath: /query")
            .And.Contain("BaseObject: SMSS_oRFF")
            .And.Contain("#%m_active%#='True' AND #%p_dcc%# NOT IN (4241) AND #%p_rel_group%# IN (68)")
            .And.Contain("id,p_code,p_name,p_description,p_rel_requester")
            .And.Contain("TenantId: 218")
            .And.Contain("SessionIdPresent: True")
            .And.Contain("SessionIdTotalLength: 68")
            .And.Contain("SessionIdSegmentCount: 2")
            .And.Contain("SessionIdSegmentLengths: 3,64")
            .And.Contain($"SerializedRequestByteLength: {Encoding.UTF8.GetByteCount(handler.Requests[0].Body)}")
            .And.Contain("query contract inbound")
            .And.Contain("HttpStatus: 200")
            .And.Contain($"ResponseByteLength: {Encoding.UTF8.GetByteCount(response)}")
            .And.Contain("QueryResultExists: True")
            .And.Contain("ErrorNo: 0")
            .And.Contain("HasErrorDescription: False")
            .And.Contain("HasErrorDetails: False")
            .And.Contain("HasItems: True")
            .And.Contain("ItemCount: 1")
            .And.Contain("TenantMetadata: 0")
            .And.Contain("PageNo: 0")
            .And.Contain("MaxPages: 0")
            .And.Contain("RecordCount: 1")
            .And.Contain("ApplicationErrorConditions: None")
            .And.NotContain(loginResult)
            .And.NotContain("Sanitized runtime value")
            .And.NotContain("sanitized-user")
            .And.NotContain("sanitized-secret")
            .And.NotContain("private description");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SourceClient_MapsTimeoutAndTransportFailureToUnavailable(bool timeout)
    {
        Exception failure = timeout ? new OperationCanceledException() : new HttpRequestException("sanitized transport failure");
        TuruncuHatOperationalRecordClient client = SourceClient(new ThrowingHandler(failure));

        Func<Task> act = async () => await client.GetActiveAsync(10, CancellationToken.None);

        ExternalIntegrationException exception = (await act.Should().ThrowAsync<ExternalIntegrationException>()).Which;
        exception.ErrorCode.Should().Be(OperationalErrorCodes.OperationalSourceUnavailable);
    }

    [Fact]
    public async Task SourceClose_RequiresExactlyOneActivity_ThenUsesReviewedFlatUpdate()
    {
        ScriptedHandler handler = new(
            Response(HttpStatusCode.OK, "{\"QueryResult\":{\"Items\":[[[{\"Value\":\"9001\"}],[{\"Value\":\"ignored\"}]]]}}"),
            Response(HttpStatusCode.OK, "{\"UpdateResult\":{\"Success\":true,\"ErrorDescription\":\"\",\"ErrorDetails\":\"\"}}"));
        TuruncuHatOperationalRecordClient client = SourceClient(handler);

        await client.CloseAsync("1001", "OR-100", "SAFE-123", CancellationToken.None);

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Path.Should().Be("/query");
        handler.Requests[1].Path.Should().Be("/update");
        handler.Requests[1].Body.Should().Contain("SAFE-123").And.Contain("m_comments");
    }

    [Fact]
    public async Task SourceClose_WithAmbiguousActivity_FailsWithoutUpdate()
    {
        const string response = "{\"QueryResult\":{\"Items\":[[[{\"Value\":\"1\"}],[{\"Value\":\"ignored\"}]],[[{\"Value\":\"2\"}],[{\"Value\":\"ignored\"}]]]}}";
        ScriptedHandler handler = new(Response(HttpStatusCode.OK, response));
        TuruncuHatOperationalRecordClient client = SourceClient(handler);

        Func<Task> act = () => client.CloseAsync("1001", "OR-100", "SAFE-123", CancellationToken.None);

        ExternalIntegrationException exception = (await act.Should().ThrowAsync<ExternalIntegrationException>()).Which;
        exception.ErrorCode.Should().Be(OperationalErrorCodes.OperationalRecordActivityAmbiguous);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task SourceClose_WithDuplicateRowsForSameActivity_RemainsAmbiguous()
    {
        const string response = "{\"QueryResult\":{\"Items\":[[[{\"Value\":\"1\"}],[{\"Value\":\"ignored\"}]],[[{\"Value\":\"1\"}],[{\"Value\":\"ignored\"}]]]}}";
        ScriptedHandler handler = new(Response(HttpStatusCode.OK, response));
        TuruncuHatOperationalRecordClient client = SourceClient(handler);

        Func<Task> act = () => client.CloseAsync("1001", "OR-100", "SAFE-123", CancellationToken.None);

        ExternalIntegrationException exception = (await act.Should().ThrowAsync<ExternalIntegrationException>()).Which;
        exception.ErrorCode.Should().Be(OperationalErrorCodes.OperationalRecordActivityAmbiguous);
    }

    [Fact]
    public async Task SourceClose_WithNoActivity_ReturnsExplicitFailure()
    {
        ScriptedHandler handler = new(Response(HttpStatusCode.OK, "{\"QueryResult\":{\"Items\":[]}}"));

        Func<Task> act = () => SourceClient(handler).CloseAsync("1001", "OR-100", "SAFE-123", CancellationToken.None);

        ExternalIntegrationException exception = (await act.Should().ThrowAsync<ExternalIntegrationException>()).Which;
        exception.ErrorCode.Should().Be(OperationalErrorCodes.OperationalRecordActivityNotFound);
    }

    [Fact]
    public async Task SourceClose_ExplicitUpdateFailure_RemainsRetryableCloseOnlyFailure()
    {
        ScriptedHandler handler = new(
            Response(HttpStatusCode.OK, "{\"QueryResult\":{\"Items\":[[[{\"Value\":\"9001\"}],[{\"Value\":\"ignored\"}]]]}}"),
            Response(HttpStatusCode.OK, "{\"UpdateResult\":{\"Success\":false}}"));

        Func<Task> act = () => SourceClient(handler).CloseAsync("1001", "OR-100", "SAFE-123", CancellationToken.None);

        ExternalIntegrationException exception = (await act.Should().ThrowAsync<ExternalIntegrationException>()).Which;
        exception.ErrorCode.Should().Be(OperationalErrorCodes.OperationalRecordCloseFailed);
        exception.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task RequesterResolver_UsesUniqueExactDisplayName_AndReturnsJiraName()
    {
        const string response = "[{\"name\":\"exact.account\",\"displayName\":\"Exact Requester\"},{\"name\":\"other\",\"displayName\":\"Other\"}]";
        ScriptedHandler handler = new(Response(HttpStatusCode.OK, response));
        CorporateJiraRequesterResolver resolver = JiraResolver(handler);

        RequesterResolutionResult result = await resolver.ResolveExactAsync("Exact Requester", CancellationToken.None);

        result.Should().Be(RequesterResolutionResult.Found("exact.account"));
        handler.Requests[0].Path.Should().Be("/rest/api/2/user/search?username=Exact%20Requester");
    }

    [Fact]
    public async Task RequesterResolver_DoesNotAcceptPartialOrUnknownMatch()
    {
        ScriptedHandler handler = new(Response(HttpStatusCode.OK, "[{\"name\":\"account.one\",\"displayName\":\"Exact Requester Extended\"}]"));
        CorporateJiraRequesterResolver resolver = JiraResolver(handler);

        RequesterResolutionResult result = await resolver.ResolveExactAsync("Exact Requester", CancellationToken.None);

        result.Status.Should().Be(RequesterResolutionStatus.NotFound);
    }

    [Fact]
    public async Task RequesterResolver_MalformedUserProjection_FailsInsteadOfReturningNotFound()
    {
        ScriptedHandler handler = new(Response(HttpStatusCode.OK, "[{\"displayName\":\"Exact Requester\"}]"));
        CorporateJiraRequesterResolver resolver = JiraResolver(handler);

        RequesterResolutionResult result = await resolver.ResolveExactAsync("Exact Requester", CancellationToken.None);

        result.Status.Should().Be(RequesterResolutionStatus.Failed);
    }

    [Fact]
    public async Task RequesterResolver_RetriesOnlySafeReadFailuresWithinConfiguredBound()
    {
        ScriptedHandler handler = new(
            Response(HttpStatusCode.ServiceUnavailable, "{}"),
            Response(HttpStatusCode.TooManyRequests, "{}"),
            Response(HttpStatusCode.OK, "[{\"name\":\"exact.account\",\"displayName\":\"Exact Requester\"}]"));
        CorporateJiraRequesterResolver resolver = JiraResolver(handler);

        RequesterResolutionResult result = await resolver.ResolveExactAsync("Exact Requester", CancellationToken.None);

        result.Status.Should().Be(RequesterResolutionStatus.Found);
        handler.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task RequesterResolver_AmbiguousExactDisplayName_FailsClosed()
    {
        const string response = "[{\"name\":\"one\",\"displayName\":\"Exact Requester\"},{\"name\":\"two\",\"displayName\":\"Exact Requester\"}]";
        CorporateJiraRequesterResolver resolver = JiraResolver(new ScriptedHandler(Response(HttpStatusCode.OK, response)));

        RequesterResolutionResult result = await resolver.ResolveExactAsync("Exact Requester", CancellationToken.None);

        result.Status.Should().Be(RequesterResolutionStatus.Ambiguous);
    }

    [Fact]
    public async Task RequesterResolver_ExhaustedProviderFailures_ReturnsFailed()
    {
        ScriptedHandler handler = new(
            Response(HttpStatusCode.ServiceUnavailable, "{}"),
            Response(HttpStatusCode.ServiceUnavailable, "{}"),
            Response(HttpStatusCode.ServiceUnavailable, "{}"));

        RequesterResolutionResult result = await JiraResolver(handler).ResolveExactAsync("Exact Requester", CancellationToken.None);

        result.Status.Should().Be(RequesterResolutionStatus.Failed);
        handler.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task JiraCreate_MapsReviewedFields_AndDoesNotSendIdempotencyAsInventedWireHeader()
    {
        ScriptedHandler handler = new(Response(HttpStatusCode.Created, "{\"key\":\"SAFE-123\"}"));
        CorporateJiraClient client = JiraClient(handler);
        JiraIssueDraft draft = new(
            Guid.NewGuid(), "OR-100", "SAFE", "Task", "OR-100 - Summary", "Description",
            "exact.account", "v1", new string('a', 64), [], FieldMapping: JiraMapping());

        JiraIssueCreationResult result = await client.CreateIssueAsync(draft, CancellationToken.None);

        result.IssueKey.Should().Be("SAFE-123");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Path.Should().Be("/rest/api/2/issue");
        using var payload = JsonDocument.Parse(handler.Requests[0].Body);
        JsonElement fields = payload.RootElement.GetProperty("fields");
        fields.GetProperty("issuetype").GetProperty("id").GetString().Should().Be("3");
        fields.GetProperty("customfield_12700").GetProperty("value").GetString().Should().Be("WASAS");
        fields.GetProperty("customfield_11500")[0].GetProperty("name").GetString().Should().Be("exact.account");
        fields.GetProperty("labels")[0].GetString().Should().Be("SunucuTalep");
        fields.TryGetProperty("reporter", out _).Should().BeFalse();
        fields.TryGetProperty("assignee", out _).Should().BeFalse();
        handler.Requests[0].Headers.Should().NotContainKey("Idempotency-Key");
    }

    [Fact]
    public async Task JiraCreate_UsesTheReviewedDraftMappingInsteadOfReadingASecondRuntimeSnapshot()
    {
        ScriptedHandler handler = new(Response(HttpStatusCode.Created, "{\"key\":\"SAFE-124\"}"));
        CorporateJiraClient client = JiraClient(handler);
        JiraIssueDraft draft = new(
            Guid.NewGuid(), "OR-101", "SDM", "Task", "Summary", "Description",
            "jira-requester", "v2", new string('a', 64), [],
            FieldMapping: new JiraIssueFieldMapping(
                "10003",
                "customfield_reviewed_team",
                "Reviewed Team",
                "customfield_reviewed_requester",
                ["ReviewedLabel"]));

        _ = await client.CreateIssueAsync(draft, CancellationToken.None);

        using var payload = JsonDocument.Parse(handler.Requests[0].Body);
        JsonElement fields = payload.RootElement.GetProperty("fields");
        fields.GetProperty("issuetype").GetProperty("id").GetString().Should().Be("10003");
        fields.GetProperty("customfield_reviewed_team").GetProperty("value").GetString().Should().Be("Reviewed Team");
        fields.GetProperty("customfield_reviewed_requester")[0].GetProperty("name").GetString().Should().Be("jira-requester");
        fields.GetProperty("labels")[0].GetString().Should().Be("ReviewedLabel");
        fields.TryGetProperty("customfield_12700", out _).Should().BeFalse();
    }

    [Fact]
    public async Task JiraCreate_WithoutReviewedDraftMapping_FailsBeforeNetworkDispatch()
    {
        ScriptedHandler handler = new();
        JiraIssueDraft draft = new(
            Guid.NewGuid(), "OR-102", "SDM", "Task", "Summary", "Description",
            null, "v1", new string('a', 64), [],
            new JiraIssueFieldMapping(string.Empty, string.Empty, string.Empty, string.Empty, []));

        Func<Task> act = () => JiraClient(handler).CreateIssueAsync(draft, CancellationToken.None);

        await act.Should().ThrowAsync<ExternalIntegrationException>()
            .Where(exception => exception.ErrorCode == OperationalErrorCodes.JiraValidationFailed
                && !exception.Retryable);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task JiraCreate_EmitsOnlyVerifiedExplicitAssignee_AndNeverReporter()
    {
        ScriptedHandler handler = new(Response(HttpStatusCode.Created, "{\"key\":\"SAFE-123\"}"));
        CorporateJiraClient client = JiraClient(handler);
        JiraIssueDraft draft = new(
            Guid.NewGuid(), "OR-100", "SDM", "Task", "Summary", "Description",
            null, "v1", new string('f', 64), [], JiraMapping(), AssigneeUsername: "verified.operator");

        _ = await client.CreateIssueAsync(draft, CancellationToken.None);

        using var payload = JsonDocument.Parse(handler.Requests[0].Body);
        JsonElement fields = payload.RootElement.GetProperty("fields");
        fields.GetProperty("assignee").GetProperty("name").GetString().Should().Be("verified.operator");
        fields.TryGetProperty("reporter", out _).Should().BeFalse();
    }

    [Fact]
    public async Task JiraCreate_EmitsVerifiedReporterSeparateFromRequesterAndIntegrationAuthorization()
    {
        ScriptedHandler handler = new(Response(HttpStatusCode.Created, "{\"key\":\"SAFE-123\"}"));
        CorporateJiraClient client = JiraClient(handler);
        JiraIssueDraft draft = new(
            Guid.NewGuid(), "OR-100", "SDM", "Task", "Summary", "Description",
            "jira-requester", "v1", new string('f', 64), [], JiraMapping(), ReporterUsername: "jira-operator");

        _ = await client.CreateIssueAsync(draft, CancellationToken.None);

        using var payload = JsonDocument.Parse(handler.Requests[0].Body);
        JsonElement fields = payload.RootElement.GetProperty("fields");
        fields.GetProperty("customfield_11500")[0].GetProperty("name").GetString().Should().Be("jira-requester");
        fields.GetProperty("reporter").GetProperty("name").GetString().Should().Be("jira-operator");
        handler.Requests[0].Authorization.Should().Be(JiraOptions().Authorization);
        handler.Requests[0].Authorization.Should().NotContain("jira-operator");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task JiraCreate_WhenJiraRejectsVerifiedReporter_ReturnsStableActionableFailure(HttpStatusCode status)
    {
        ScriptedHandler handler = new(Response(status, "{}"));
        JiraIssueDraft draft = new(
            Guid.NewGuid(), "OR-100", "SDM", "Task", "Summary", "Description",
            null, "v1", new string('f', 64), [], JiraMapping(), ReporterUsername: "jira-operator");

        Func<Task> act = async () => await JiraClient(handler).CreateIssueAsync(draft, CancellationToken.None);

        await act.Should().ThrowAsync<ExternalIntegrationException>()
            .Where(exception => exception.ErrorCode == OperationalErrorCodes.JiraReporterRejected
                && !exception.Retryable
                && !exception.OutcomeUnknown);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task JiraCreate_ServerFailure_IsOutcomeUnknown_AndIsNotRetried()
    {
        ScriptedHandler handler = new(Response(HttpStatusCode.InternalServerError, "{}"));
        CorporateJiraClient client = JiraClient(handler);
        JiraIssueDraft draft = new(Guid.NewGuid(), "OR-100", "SAFE", "Task", "Summary", "Description", null, "v1", new string('b', 64), [], FieldMapping: JiraMapping());

        Func<Task> act = async () => await client.CreateIssueAsync(draft, CancellationToken.None);

        ExternalIntegrationException exception = (await act.Should().ThrowAsync<ExternalIntegrationException>()).Which;
        exception.OutcomeUnknown.Should().BeTrue();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task JiraCreate_OmitsRequesterFieldWhenNoExactAccountWasResolved()
    {
        ScriptedHandler handler = new(Response(HttpStatusCode.Created, "{\"key\":\"SAFE-123\"}"));
        CorporateJiraClient client = JiraClient(handler);
        JiraIssueDraft draft = new(Guid.NewGuid(), "OR-100", "SAFE", "Task", "Summary", "Description", null, "v1", new string('c', 64), [], FieldMapping: JiraMapping());

        _ = await client.CreateIssueAsync(draft, CancellationToken.None);

        handler.Requests[0].Body.Should().NotContain("customfield_requester");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, OperationalErrorCodes.JiraValidationFailed, false, false)]
    [InlineData(HttpStatusCode.Unauthorized, OperationalErrorCodes.JiraUnauthorized, false, false)]
    [InlineData(HttpStatusCode.TooManyRequests, OperationalErrorCodes.JiraUnavailable, true, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, OperationalErrorCodes.JiraUnavailable, false, true)]
    public async Task JiraCreate_ClassifiesReviewedHttpFailureCategories(
        HttpStatusCode status,
        string code,
        bool retryable,
        bool outcomeUnknown)
    {
        ScriptedHandler handler = new(Response(status, "{}"));
        JiraIssueDraft draft = new(Guid.NewGuid(), "OR-100", "SAFE", "Task", "Summary", "Description", null, "v1", new string('d', 64), [], FieldMapping: JiraMapping());

        Func<Task> act = async () => await JiraClient(handler).CreateIssueAsync(draft, CancellationToken.None);

        ExternalIntegrationException exception = (await act.Should().ThrowAsync<ExternalIntegrationException>()).Which;
        exception.ErrorCode.Should().Be(code);
        exception.Retryable.Should().Be(retryable);
        exception.OutcomeUnknown.Should().Be(outcomeUnknown);
        handler.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task JiraCreate_TimeoutOrTransportFailure_IsUnknownAndNeverRetried(bool timeout)
    {
        Exception failure = timeout ? new OperationCanceledException() : new HttpRequestException("sanitized transport failure");
        CorporateJiraClient client = JiraClient(new ThrowingHandler(failure));
        JiraIssueDraft draft = new(Guid.NewGuid(), "OR-100", "SAFE", "Task", "Summary", "Description", null, "v1", new string('e', 64), [], FieldMapping: JiraMapping());

        Func<Task> act = async () => await client.CreateIssueAsync(draft, CancellationToken.None);

        ExternalIntegrationException exception = (await act.Should().ThrowAsync<ExternalIntegrationException>()).Which;
        exception.OutcomeUnknown.Should().BeTrue();
    }

    [Fact]
    public void TuruncuClassifier_RequiresManualReviewEvenForValidSourceProjection()
    {
        TuruncuHatOperationalRecordClassifier classifier = new();
        OperationalRecordSourceItem valid = Source("1001", true);
        OperationalRecordSourceItem invalid = Source("not-numeric", true);

        OperationalRecordClassificationResult validResult = classifier.Classify(valid);
        validResult.Classification.Should().Be(OperationalRecordClassification.NeedsManualReview);
        validResult.JiraEligible.Should().BeFalse();
        classifier.Classify(invalid).JiraEligible.Should().BeFalse();
    }

    [Fact]
    public async Task JiraCreate_InReadOnlyIntegrationMode_IsBlockedBeforeNetworkDispatch()
    {
        ScriptedHandler handler = new();
        CorporateJiraClient client = JiraClient(handler, readOnlyIntegrationMode: true);
        JiraIssueDraft draft = new(
            Guid.NewGuid(),
            "OR-100",
            "SAFE",
            "Task",
            "Summary",
            "Description",
            null,
            "v1",
            new string('a', 64),
            [],
            JiraMapping());

        Func<Task> act = () => client.CreateIssueAsync(draft, CancellationToken.None);

        await act.Should().ThrowAsync<ExternalIntegrationException>()
            .Where(exception => exception.ErrorCode == OperationalErrorCodes.ExternalWritesDisabled);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SourceClose_InReadOnlyIntegrationMode_IsBlockedBeforeNetworkDispatch()
    {
        ScriptedHandler handler = new();
        TuruncuHatOperationalRecordClient client = SourceClient(handler, readOnlyIntegrationMode: true);

        Func<Task> act = () => client.CloseAsync("1001", "OR-100", "SAFE-123", CancellationToken.None);

        await act.Should().ThrowAsync<ExternalIntegrationException>()
            .Where(exception => exception.ErrorCode == OperationalErrorCodes.ExternalWritesDisabled);
        handler.Requests.Should().BeEmpty();
    }

    private static TuruncuHatSessionManager SessionManager(ScriptedHandler handler) => new(
        Client(handler, "https://source.invalid/"),
        Options.Create(TuruncuOptions()),
        TimeProvider.System,
        new EnterpriseIntegrationHealthState(),
        new EnterpriseIntegrationTelemetry(),
        NullLogger<TuruncuHatSessionManager>.Instance);

    private static TuruncuHatOperationalRecordClient SourceClient(
        HttpMessageHandler handler,
        bool readOnlyIntegrationMode = false,
        ILogger<TuruncuHatOperationalRecordClient>? logger = null,
        bool diagnosticContractLogging = false,
        string session = "sanitized-session")
    {
        TuruncuHatOptions options = TuruncuOptions();
        options.DiagnosticContractLogging = diagnosticContractLogging;
        return new TuruncuHatOperationalRecordClient(
            Client(handler, "https://source.invalid/"),
            new FixedSessionManager(session),
            Options.Create(options),
            Options.Create(new OperationalRecordsOptions { ReadOnlyIntegrationMode = readOnlyIntegrationMode }),
            new EnterpriseIntegrationHealthState(),
            new EnterpriseIntegrationTelemetry(),
            logger ?? NullLogger<TuruncuHatOperationalRecordClient>.Instance);
    }

    private static CorporateJiraRequesterResolver JiraResolver(ScriptedHandler handler) => new(
        Client(handler, "https://jira.invalid/"),
        Options.Create(JiraOptions()),
        TimeProvider.System,
        new EnterpriseIntegrationHealthState(),
        new EnterpriseIntegrationTelemetry(),
        NullLogger<CorporateJiraRequesterResolver>.Instance);

    private static CorporateJiraClient JiraClient(
        HttpMessageHandler handler,
        bool readOnlyIntegrationMode = false) => new(
        Client(handler, "https://jira.invalid/"),
        Options.Create(JiraOptions()),
        Options.Create(new OperationalRecordsOptions { ReadOnlyIntegrationMode = readOnlyIntegrationMode }),
        new EnterpriseIntegrationHealthState(),
        new EnterpriseIntegrationTelemetry(),
        NullLogger<CorporateJiraClient>.Instance);

    private static TuruncuHatOptions TuruncuOptions() => new()
    {
        Authorization = "Sanitized runtime value",
        Username = "sanitized-user",
        Password = "sanitized-secret",
        TenantId = 218,
        SourceBaseObject = "SMSS_oRFF",
        RelatedGroupId = 68,
        ExcludedDccIds = [4241],
        ActivityBaseObject = "BPM_Actvty",
        ActivityTaskModelId = 10,
        ActivityGroupId = 20,
        ActivityMainObjectTypeId = 30,
        CompletedStatusId = 40,
        CompletionCommentTemplate = "Transferred to {JiraKey}",
        SessionLifetimeSeconds = 60
    };

    private static JiraIntegrationOptions JiraOptions() => new()
    {
        Authorization = "Sanitized runtime value",
        ProjectKey = "SAFE",
        IssueTypeId = "3",
        TeamCustomField = "customfield_12700",
        TeamValue = "WASAS",
        RequesterWatcherCustomField = "customfield_11500",
        Labels = ["SunucuTalep"],
        UserSearchRetryDelayMilliseconds = 0
    };

    private static JiraIssueFieldMapping JiraMapping() => new(
        "3",
        "customfield_12700",
        "WASAS",
        "customfield_11500",
        ["SunucuTalep"]);

    private static OperationalRecordSourceItem Source(string id, bool isOpen) => new(
        id, "OR-100", "Title", "Description", "Requester", null, null, null, null, isOpen, null, null);

    private static HttpClient Client(HttpMessageHandler handler, string baseAddress) => new(handler)
    {
        BaseAddress = new Uri(baseAddress),
        Timeout = Timeout.InfiniteTimeSpan
    };

    private static HttpResponseMessage Response(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class FixedSessionManager(string session = "sanitized-session") : ITuruncuHatSessionManager
    {
        public Task<string> GetSessionAsync(CancellationToken cancellationToken) => Task.FromResult(session);
        public void Invalidate(string rejectedSession) { }
    }

    private sealed class ScriptedHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.RequestUri!.PathAndQuery,
                body,
                request.Content?.Headers.ContentType?.ToString(),
                request.Headers.TryGetValues("Authorization", out IEnumerable<string>? authorization) ? authorization.Single() : null,
                request.Headers.ToDictionary(header => header.Key, header => string.Join(',', header.Value), StringComparer.OrdinalIgnoreCase)));
            return _responses.Dequeue();
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed record CapturedRequest(
        string Path,
        string Body,
        string? ContentType,
        string? Authorization,
        IReadOnlyDictionary<string, string> Headers);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
