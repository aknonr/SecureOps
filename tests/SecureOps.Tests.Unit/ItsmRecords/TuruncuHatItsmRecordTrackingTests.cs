using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SecureOps.Infrastructure.ItsmRecords;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;
using static SecureOps.Tests.Unit.ItsmRecords.TuruncuHatItsmRecordFixtures;
using static SecureOps.Tests.Unit.ItsmRecords.TuruncuHatItsmTestHttp;
using static SecureOps.Tests.Unit.ItsmRecords.TuruncuHatItsmTrackingFixtures;

namespace SecureOps.Tests.Unit.ItsmRecords;

public sealed class TuruncuHatItsmRecordTrackingTests
{
    private const string _session = "synthetic-session";

    [Fact]
    public async Task Found_ReturnsHeaderActivitiesAndCommentSummary_ThreeReadOnlyQueries()
    {
        ScriptedHandler handler = new(
            Ok(Query(OrHeaderRow)),
            Ok(Query(Activity("910002", completed: null), Activity("910001", completed: "2026-01-05T01:45:00"))),
            Ok(Query(Comment("300", "2026-01-05T02:30:00"), Comment("120", "2026-01-06T09:00:00"))));

        ItsmRecordTrackingResult result = await Tracking(handler).GetOrTrackingAsync(OrSourceId, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Found);
        result.Provider.Should().Be("TuruncuHat");
        result.State.Should().Be(new ItsmOrState("2026-01-05T02:30:00", null, null));
        result.Activities!.State.Should().Be(ItsmTrackingPartState.Known);
        result.Activities.Value.Should().Equal(
            new ItsmActivity("910001", "8301", "8501", "2026-01-05T01:00:00", "2026-01-05T01:45:00"),
            new ItsmActivity("910002", "8301", "8501", "2026-01-05T01:00:00", null));
        // The newest comment is the highest id, not the latest-looking time text.
        result.Comments!.Value.Should().Be(new ItsmCommentSummary(2, "2026-01-05T02:30:00"));
        handler.Requests.Select(r => r.Path).Should().Equal("/api/query", "/api/query", "/api/query");
    }

    [Fact]
    public async Task Requests_AreSelectOnly_NoEncode_AndNeverSelectPersonOrCommentTextFields()
    {
        ScriptedHandler handler = new(Ok(Query(OrHeaderRow)), Ok(Query()), Ok(Query()));

        await Tracking(handler).GetOrTrackingAsync(" " + OrSourceId + " ", CancellationToken.None);

        JsonElement header = Req(handler, 0);
        header.GetProperty("BaseObject").GetString().Should().Be("SyntheticRequest");
        Strings(header, "Filters").Should().Equal("#%m_active%#='True' AND #%id%#=" + OrSourceId);
        Strings(header, "Selects").Should().Equal("id", "m_last_mod_dt");

        JsonElement activities = Req(handler, 1);
        activities.GetProperty("BaseObject").GetString().Should().Be("BPM_Actvty");
        Strings(activities, "Filters").Should().Equal(
            "#%m_bpe_actvty_task_model.m_action_type.m_code%#!='WorkflowSystemAction' AND #%m_process.m_main_object_id%#=" + OrSourceId);
        Strings(activities, "Selects").Should().Equal("id", "m_group", "m_status", "m_created_dt", "m_completion_date");

        JsonElement comments = Req(handler, 2);
        comments.GetProperty("BaseObject").GetString().Should().Be("cmn_comment");
        Strings(comments, "Filters").Should().Equal("#%m_active%#='True' AND #%m_mob%#=4711 AND #%m_mid%#=" + OrSourceId);
        Strings(comments, "Selects").Should().Equal("id", "m_created_dt");

        foreach ((string _, string body) in handler.Requests)
        {
            JsonElement req = JsonDocument.Parse(body).RootElement.GetProperty("req");
            req.GetProperty("NoEncode").GetBoolean().Should().BeTrue();
            req.TryGetProperty("QueryPermissions", out _).Should().BeFalse();
            body.Should().NotContainAny("m_done_by", "\"m_comment\"", "requester", "affected_end_user", "Password", "Username");
        }
    }

    [Fact]
    public async Task ConfiguredStatusAndStageFields_AreSelected_AndReturnedRaw()
    {
        ScriptedHandler handler = new(
            Ok(Query([.. OrHeaderRow, ("SET.p_synthetic_status", "9301"), ("SET.p_synthetic_stage", "9401")])), Ok(Query()), Ok(Query()));

        ItsmRecordTrackingResult result = await Tracking(handler, statusField: "p_synthetic_status", stageField: "p_synthetic_stage")
            .GetOrTrackingAsync(OrSourceId, CancellationToken.None);

        Strings(Req(handler, 0), "Selects").Should().Equal("id", "m_last_mod_dt", "p_synthetic_status", "p_synthetic_stage");
        result.State.Should().Be(new ItsmOrState("2026-01-05T02:30:00", "9301", "9401"));
    }

    [Fact]
    public async Task EmptyOptionalHeaderValues_AreNull_NotMisread()
    {
        ScriptedHandler handler = new(Ok(Query([("SET.id", OrSourceId), ("SET.m_last_mod_dt", null)])), Ok(Query()), Ok(Query()));

        ItsmRecordTrackingResult result = await Tracking(handler).GetOrTrackingAsync(OrSourceId, CancellationToken.None);

        result.State.Should().Be(new ItsmOrState(null, null, null));
    }

    [Fact]
    public async Task HeaderWithZeroRows_IsNotFound_AndNoFurtherQuery()
    {
        ScriptedHandler handler = new(Ok(Query()));

        ItsmRecordTrackingResult result = await Tracking(handler).GetOrTrackingAsync(OrSourceId, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.NotFound);
        result.Activities.Should().BeNull();
        result.Comments.Should().BeNull();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task HeaderWithTwoRows_IsAmbiguous()
    {
        ItsmRecordTrackingResult result = await Tracking(new ScriptedHandler(Ok(Query(OrHeaderRow, OrHeaderRow))))
            .GetOrTrackingAsync(OrSourceId, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Ambiguous);
        result.State.Should().BeNull();
    }

    [Fact]
    public async Task HeaderRowForAnotherId_IsUnknownInvalidResponse()
    {
        ItsmRecordTrackingResult result = await Tracking(new ScriptedHandler(Ok(Query(OrHeaderRow.With("SET.id", "700999")))))
            .GetOrTrackingAsync(OrSourceId, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.InvalidResponse);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, ItsmRecordLookupReason.Unavailable)]
    [InlineData(HttpStatusCode.BadRequest, ItsmRecordLookupReason.Rejected)]
    public async Task HeaderHttpFailure_IsUnknown_NeverNotFound(HttpStatusCode status, ItsmRecordLookupReason reason)
    {
        ItsmRecordTrackingResult result = await Tracking(new ScriptedHandler(Response(status, "{}")))
            .GetOrTrackingAsync(OrSourceId, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Unknown);
        result.Reason.Should().Be(reason);
    }

    [Fact]
    public async Task ActivityFailure_IsUnknownPart_WithoutHidingHeaderOrComments()
    {
        ScriptedHandler handler = new(
            Ok(Query(OrHeaderRow)), Response(HttpStatusCode.InternalServerError, "{}"), Ok(Query(Comment("5", "2026-01-05T02:30:00"))));

        ItsmRecordTrackingResult result = await Tracking(handler).GetOrTrackingAsync(OrSourceId, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Found);
        result.Activities!.State.Should().Be(ItsmTrackingPartState.Unknown);
        result.Activities.Reason.Should().Be(ItsmRecordLookupReason.Unavailable);
        result.Activities.Value.Should().BeNull();
        result.Comments!.State.Should().Be(ItsmTrackingPartState.Known);
    }

    [Fact]
    public async Task ZeroActivitiesAndComments_AreKnownEmpty_NotUnknown()
    {
        ItsmRecordTrackingResult result = await Tracking(new ScriptedHandler(Ok(Query(OrHeaderRow)), Ok(Query()), Ok(Query())))
            .GetOrTrackingAsync(OrSourceId, CancellationToken.None);

        result.Activities!.State.Should().Be(ItsmTrackingPartState.Known);
        result.Activities.Value.Should().BeEmpty();
        result.Comments!.Value.Should().Be(new ItsmCommentSummary(0, null));
    }

    [Fact]
    public async Task ActivityWithoutCompletionKey_ReadsAsOpen_ButAMissingRequiredKeyIsIncomplete()
    {
        ScriptedHandler open = new(Ok(Query(OrHeaderRow)),
            Ok(Query(Activity("1").Where(cell => cell.Key != "SET.m_completion_date").ToArray())), Ok(Query()));
        ScriptedHandler broken = new(Ok(Query(OrHeaderRow)),
            Ok(Query(Activity("1").Where(cell => cell.Key != "SET.m_status").ToArray())), Ok(Query()));

        ItsmRecordTrackingResult openResult = await Tracking(open).GetOrTrackingAsync(OrSourceId, CancellationToken.None);
        ItsmRecordTrackingResult brokenResult = await Tracking(broken).GetOrTrackingAsync(OrSourceId, CancellationToken.None);

        openResult.Activities!.Value!.Single().CompletedText.Should().BeNull();
        brokenResult.Activities!.State.Should().Be(ItsmTrackingPartState.Unknown);
        brokenResult.Activities.Reason.Should().Be(ItsmRecordLookupReason.IncompleteResponse);
    }

    [Fact]
    public async Task MoreActivitiesThanTheCap_AreUnknownIncomplete_NotTruncated()
    {
        (string Key, string? Value)[][] rows = [.. Enumerable.Range(1, TuruncuHatItsmRecordTracking.MaxActivities + 1)
            .Select(i => Activity(i.ToString()))];
        string body = VendorEnvelope(0, 0, rows.Length, rows);

        ItsmRecordTrackingResult result = await Tracking(new ScriptedHandler(Ok(Query(OrHeaderRow)), Ok(body), Ok(Query())))
            .GetOrTrackingAsync(OrSourceId, CancellationToken.None);

        result.Activities!.State.Should().Be(ItsmTrackingPartState.Unknown);
        result.Activities.Reason.Should().Be(ItsmRecordLookupReason.IncompleteResponse);
    }

    [Fact]
    public async Task ActivitiesOnMorePages_AreUnknownIncomplete_WhileAnExactCapIsKnown()
    {
        string paged = VendorEnvelope(0, 69, 690, Activity("1"), Activity("2"));
        (string Key, string? Value)[][] exact = [.. Enumerable.Range(1, TuruncuHatItsmRecordTracking.MaxActivities).Select(i => Activity(i.ToString()))];

        ItsmRecordTrackingResult pagedResult = await Tracking(new ScriptedHandler(Ok(Query(OrHeaderRow)), Ok(paged), Ok(Query())))
            .GetOrTrackingAsync(OrSourceId, CancellationToken.None);
        ItsmRecordTrackingResult exactResult = await Tracking(new ScriptedHandler(
                Ok(Query(OrHeaderRow)), Ok(VendorEnvelope(0, 0, exact.Length, exact)), Ok(Query())))
            .GetOrTrackingAsync(OrSourceId, CancellationToken.None);

        pagedResult.Activities!.Reason.Should().Be(ItsmRecordLookupReason.IncompleteResponse);
        exactResult.Activities!.Value.Should().HaveCount(TuruncuHatItsmRecordTracking.MaxActivities);
    }

    [Fact]
    public async Task ConfiguredActivityMainObject_IsAddedToTheActivityFilter()
    {
        ScriptedHandler handler = new(Ok(Query(OrHeaderRow)), Ok(Query()), Ok(Query()));

        await Tracking(handler, activityMainObject: "55").GetOrTrackingAsync(OrSourceId, CancellationToken.None);

        Strings(Req(handler, 1), "Filters").Should().Equal(
            "#%m_bpe_actvty_task_model.m_action_type.m_code%#!='WorkflowSystemAction' AND #%m_process.m_main_object_id%#="
            + OrSourceId + " AND #%m_process.m_main_object%#=55");
    }

    [Fact]
    public async Task UnconfiguredCommentMainObject_IsUnknownPartNotConfigured_WithoutACommentQuery()
    {
        ScriptedHandler handler = new(Ok(Query(OrHeaderRow)), Ok(Query()));

        ItsmRecordTrackingResult result = await Tracking(handler, commentMainObject: "").GetOrTrackingAsync(OrSourceId, CancellationToken.None);

        result.Comments!.State.Should().Be(ItsmTrackingPartState.Unknown);
        result.Comments.Reason.Should().Be(ItsmRecordLookupReason.NotConfigured);
        result.Activities!.State.Should().Be(ItsmTrackingPartState.Known);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task NonNumericCommentId_IsUnknownPartInvalidResponse()
    {
        ScriptedHandler handler = new(Ok(Query(OrHeaderRow)), Ok(Query()), Ok(Query(Comment("abc", "2026-01-05T02:30:00"))));

        ItsmRecordTrackingResult result = await Tracking(handler).GetOrTrackingAsync(OrSourceId, CancellationToken.None);

        result.Comments!.Reason.Should().Be(ItsmRecordLookupReason.InvalidResponse);
    }

    [Fact]
    public async Task CommentsOnMorePages_AreUnknownIncomplete_NotACount()
    {
        ScriptedHandler handler = new(Ok(Query(OrHeaderRow)), Ok(Query()),
            Ok(VendorEnvelope(0, 3, 25, Comment("1", "t1"), Comment("2", "t2"))));

        ItsmRecordTrackingResult result = await Tracking(handler).GetOrTrackingAsync(OrSourceId, CancellationToken.None);

        result.Comments!.State.Should().Be(ItsmTrackingPartState.Unknown);
        result.Comments.Reason.Should().Be(ItsmRecordLookupReason.IncompleteResponse);
    }

    [Fact]
    public async Task VendorSinglePageEnvelope_ZeroBasedPageNoAndMaxPagesZero_IsComplete()
    {
        ScriptedHandler handler = new(
            Ok(VendorEnvelope(0, 0, 1, OrHeaderRow)),
            Ok(VendorEnvelope(0, 0, 1, Activity("1"))),
            Ok(VendorEnvelope(0, 0, 1, Comment("9", "2026-01-05T02:30:00"))));

        ItsmRecordTrackingResult result = await Tracking(handler).GetOrTrackingAsync(OrSourceId, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Found);
        result.Activities!.State.Should().Be(ItsmTrackingPartState.Known);
        result.Comments!.Value!.Count.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0000")]
    [InlineData("12 3")]
    [InlineData("12'3")]
    [InlineData("1 OR 1=1")]
    [InlineData("#%id%#")]
    [InlineData("١٢٣")]
    [InlineData("123456789012345678901")]
    [InlineData(null)]
    public async Task InvalidSourceId_IsUnknownInvalidIdentifier_WithoutAnyRequest(string? sourceId)
    {
        ScriptedHandler handler = new();
        ITuruncuHatSessionManager sessions = Sessions();

        ItsmRecordTrackingResult result = await Tracking(handler, sessions: sessions).GetOrTrackingAsync(sourceId, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.InvalidIdentifier);
        result.SourceId.Should().BeNull();
        handler.Requests.Should().BeEmpty();
        await sessions.DidNotReceive().GetSessionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnconfiguredOrBaseObject_IsUnknownNotConfigured_WithoutAnyRequest()
    {
        ScriptedHandler handler = new();

        ItsmRecordTrackingResult result = await Tracking(handler, orBaseObject: "").GetOrTrackingAsync(OrSourceId, CancellationToken.None);

        result.Reason.Should().Be(ItsmRecordLookupReason.NotConfigured);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CallerCancellation_Propagates_BeforeAnyRequest()
    {
        ScriptedHandler handler = new();
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        Func<Task> act = () => Tracking(handler).GetOrTrackingAsync(OrSourceId, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task DependencyTimeout_IsUnknownTimeout()
    {
        ItsmRecordTrackingResult result = await Tracking(new HangingHandler(), requestTimeoutSeconds: 1)
            .GetOrTrackingAsync(OrSourceId, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.Timeout);
    }

    [Fact]
    public async Task Logs_NeverContainSessionCredentialsUrlIdOrValues()
    {
        CapturingLogger<TuruncuHatItsmRecordTracking> logger = new();
        ScriptedHandler handler = new(
            Response(HttpStatusCode.InternalServerError, "{}"),
            Ok(Query(OrHeaderRow)), Response(HttpStatusCode.BadRequest, "{}"), Ok(ApplicationError),
            Ok(Query(OrHeaderRow)), Ok(Query(Activity("1").Where(cell => cell.Key != "SET.m_status").ToArray())),
            Ok(Query(Comment("abc", "2026-01-05T02:30:00"))));
        TuruncuHatItsmRecordTracking tracking = Tracking(handler, logger: logger);

        await tracking.GetOrTrackingAsync(OrSourceId, CancellationToken.None);
        await tracking.GetOrTrackingAsync(OrSourceId, CancellationToken.None);
        await tracking.GetOrTrackingAsync(OrSourceId, CancellationToken.None);

        logger.Messages.Should().NotBeEmpty();
        string all = string.Join('\n', logger.Messages);
        all.Should().NotContainAny(_session, "synthetic-user", "synthetic-password", "Basic c3ludGhldGlj", "itsm.invalid",
            OrSourceId, "2026-01-05", "8301");
    }

    private static TuruncuHatItsmRecordTracking Tracking(
        HttpMessageHandler handler,
        ITuruncuHatSessionManager? sessions = null,
        int requestTimeoutSeconds = 30,
        string orBaseObject = "SyntheticRequest",
        string statusField = "",
        string stageField = "",
        string commentMainObject = "4711",
        string activityMainObject = "",
        ILogger<TuruncuHatItsmRecordTracking>? logger = null) =>
        new(Client(handler), sessions ?? Sessions(),
            Options.Create(new TuruncuHatOptions
            {
                BaseUrl = BaseUrl,
                Authorization = "Basic c3ludGhldGljOnN5bnRoZXRpYw==",
                Username = "synthetic-user",
                Password = "synthetic-password",
                TenantId = 7,
                SessionLifetimeSeconds = 600,
                RequestTimeoutSeconds = requestTimeoutSeconds,
                MaxResponseBytes = 1_048_576
            }),
            Options.Create(new ItsmRecordLookupOptions
            {
                Provider = "TuruncuHat",
                OrBaseObject = orBaseObject,
                OcoBaseObject = "SyntheticChange",
                OrStatusField = statusField,
                OrStageField = stageField,
                CommentMainObject = commentMainObject,
                ActivityMainObject = activityMainObject
            }),
            new EnterpriseIntegrationTelemetry(),
            logger ?? NullLogger<TuruncuHatItsmRecordTracking>.Instance);

    private static ITuruncuHatSessionManager Sessions()
    {
        ITuruncuHatSessionManager sessions = Substitute.For<ITuruncuHatSessionManager>();
        sessions.GetSessionAsync(Arg.Any<CancellationToken>()).Returns(_session);
        return sessions;
    }

    private static JsonElement Req(ScriptedHandler handler, int index) =>
        JsonDocument.Parse(handler.Requests[index].Body).RootElement.GetProperty("req").Clone();

    private static IEnumerable<string?> Strings(JsonElement req, string property) =>
        req.GetProperty(property).EnumerateArray().Select(element => element.GetString());
}
