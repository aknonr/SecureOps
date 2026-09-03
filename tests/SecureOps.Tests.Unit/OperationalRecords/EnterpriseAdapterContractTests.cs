using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

        first.Should().Be("sanitized-session");
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
              "Items":[[[{"Key":"p_description","Value":"Detail &lt;encoded&gt;"}],
                         [{"Key":"id","Value":"1001"}],
                         [{"Key":"p_rel_requester","Value":"Exact Requester"}],
                         [{"Key":"p_name","Value":"Short &amp; safe"}],
                         [{"Key":"p_code","Value":"OR-100"}]]],
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
    public async Task SourceClient_KeyedProjection_RejectsUnexpectedOrDuplicateKeys()
    {
        const string response = """
            {"QueryResult":{"Items":[
              [[{"Key":"id","Value":"1001"}],[{"Key":"p_code","Value":"OR-100"}],[{"Key":"p_name","Value":"Title"}],[{"Key":"p_description","Value":"Description"}],[{"Key":"unexpected","Value":"Requester"}]],
              [[{"Key":"id","Value":"1002"}],[{"Key":"p_code","Value":"OR-200"}],[{"Key":"p_name","Value":"Title"}],[{"Key":"p_name","Value":"Description"}],[{"Key":"p_rel_requester","Value":"Requester"}]]
            ]}}
            """;

        IReadOnlyList<OperationalRecordSourceItem> records = await SourceClient(
            new ScriptedHandler(Response(HttpStatusCode.OK, response))).GetActiveAsync(10, CancellationToken.None);

        records.Should().BeEmpty();
    }

    [Fact]
    public async Task SourceClient_PartialEvidencedEnvelope_FailsClosed()
    {
        const string response = "{\"QueryResult\":{\"ErrorNo\":0,\"Items\":[]}}";

        Func<Task> act = async () => await SourceClient(
            new ScriptedHandler(Response(HttpStatusCode.OK, response))).GetActiveAsync(10, CancellationToken.None);

        ExternalIntegrationException exception = (await act.Should().ThrowAsync<ExternalIntegrationException>()).Which;
        exception.ErrorCode.Should().Be(OperationalErrorCodes.OperationalRecordQueryFailed);
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
    public void TuruncuClassifier_AcceptsOnlyValidReviewedSourceProjection()
    {
        TuruncuHatOperationalRecordClassifier classifier = new();
        OperationalRecordSourceItem valid = Source("1001", true);
        OperationalRecordSourceItem invalid = Source("not-numeric", true);

        classifier.Classify(valid).JiraEligible.Should().BeTrue();
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
        bool readOnlyIntegrationMode = false) => new(
        Client(handler, "https://source.invalid/"),
        new FixedSessionManager(),
        Options.Create(TuruncuOptions()),
        Options.Create(new OperationalRecordsOptions { ReadOnlyIntegrationMode = readOnlyIntegrationMode }),
        new EnterpriseIntegrationHealthState(),
        new EnterpriseIntegrationTelemetry(),
        NullLogger<TuruncuHatOperationalRecordClient>.Instance);

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

    private sealed class FixedSessionManager : ITuruncuHatSessionManager
    {
        public Task<string> GetSessionAsync(CancellationToken cancellationToken) => Task.FromResult("sanitized-session");
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
        string? Authorization,
        IReadOnlyDictionary<string, string> Headers);
}
