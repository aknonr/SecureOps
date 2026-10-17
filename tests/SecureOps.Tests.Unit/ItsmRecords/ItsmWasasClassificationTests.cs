using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SecureOps.Infrastructure;
using SecureOps.Infrastructure.InUse;
using SecureOps.Infrastructure.ItsmRecords;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.InUse;
using static SecureOps.Tests.Unit.ItsmRecords.TuruncuHatItsmRecordFixtures;
using static SecureOps.Tests.Unit.ItsmRecords.TuruncuHatItsmTestHttp;
using static SecureOps.Tests.Unit.ItsmRecords.TuruncuHatItsmTrackingFixtures;

namespace SecureOps.Tests.Unit.ItsmRecords;

/// <summary>
/// TH-11: the read-only WASAS classification of one OR. Every id, code and label is a synthetic placeholder
/// (group 7001 is the synthetic WASAS group, 7002 another team); nothing comes from a real system.
/// </summary>
public sealed class ItsmWasasClassificationTests
{
    private const string _session = "synthetic-session";
    private const string _wasasGroup = "7001";
    private const string _otherGroup = "7002";
    private const string _review = "Synthetic review task";

    private static ItsmRecordLookupOptions Options(Action<ItsmRecordLookupOptions>? change = null)
    {
        ItsmRecordLookupOptions options = new()
        {
            Provider = "TuruncuHat",
            OrBaseObject = "SyntheticRequest",
            OcoBaseObject = "SyntheticChange",
            OrStageField = "p_synthetic_stage",
            OrStatusField = "p_synthetic_status",
            OrAssignedGroupField = "p_synthetic_group",
            WasasGroupId = _wasasGroup,
            ActivityTaskModelContains = ["review task"],
            ClosedStageIds = ["9100"],
            ClosedStatusIds = ["9200"]
        };
        change?.Invoke(options);
        return options;
    }

    private static ItsmOrState State(string? stage = "9001", string? status = "9002", string? group = _wasasGroup) =>
        new("2026-01-05T02:30:00", status, stage, null, null, group);

    private static ItsmActivity Row(string id = "1", string? group = _wasasGroup, string? status = "4", string? completed = "2026-01-05T01:45:00",
        string? label = _review, string? created = "2026-01-05T01:00:00") => new(id, group, status, created, completed, label);

    private static ItsmTrackingPart<IReadOnlyList<ItsmActivity>> Known(params ItsmActivity[] rows) =>
        ItsmTrackingPart<IReadOnlyList<ItsmActivity>>.Known(rows);

    private static ItsmWasasClassification Classify(ItsmRecordLookupOptions options, ItsmOrState state, ItsmTrackingPart<IReadOnlyList<ItsmActivity>> activities) =>
        ItsmWasasClassifier.Classify(options, state, activities, "Test");

    // ----- the pure rules -----

    [Theory]
    [InlineData("4", "2026-01-05T01:45:00", ItsmWasasActivityClass.Completed)]
    [InlineData("4", null, ItsmWasasActivityClass.Pending)]
    [InlineData("4", "", ItsmWasasActivityClass.Pending)]
    [InlineData("5", "2026-01-05T01:45:00", ItsmWasasActivityClass.NotApproved)]
    [InlineData("99", "2026-01-05T01:45:00", ItsmWasasActivityClass.NotApproved)]
    [InlineData("1", null, ItsmWasasActivityClass.Pending)]
    [InlineData(null, "2026-01-05T01:45:00", ItsmWasasActivityClass.Unknown)]
    public void Activity_SingleWasasRow_IsClassifiedByCompletionTimeAndStatusCode(string? status, string? completed, ItsmWasasActivityClass expected)
    {
        ItsmWasasClassification result = Classify(Options(), State(), Known(Row(status: status, completed: completed)));

        result.Activity.Should().Be(expected);
        result.Provider.Should().Be("Test");
    }

    [Fact]
    public void Activity_ApprovedStatusCode_IsConfigurable_AndTheDefaultIsFour()
    {
        new ItsmRecordLookupOptions().ActivityApprovedStatus.Should().Be("4");

        Classify(Options(o => o.ActivityApprovedStatus = "6"), State(), Known(Row(status: "6"))).Activity.Should().Be(ItsmWasasActivityClass.Completed);
        Classify(Options(o => o.ActivityApprovedStatus = "6"), State(), Known(Row(status: "4"))).Activity.Should().Be(ItsmWasasActivityClass.NotApproved);
    }

    [Fact]
    public void Activity_OtherGroupRowsAndOtherTasks_AreIgnored_ButNeverCountedAsNone()
    {
        // The WASAS row is the only candidate even though another team's completed row and another task exist.
        ItsmWasasClassification one = Classify(Options(), State(), Known(
            Row("1", group: _otherGroup), Row("2", label: "Another task"), Row("3", completed: null)));
        one.Activity.Should().Be(ItsmWasasActivityClass.Pending);

        // No candidate at all is Unknown, never "no activity" or Pending.
        Classify(Options(), State(), Known(Row("1", group: _otherGroup))).Activity.Should().Be(ItsmWasasActivityClass.Unknown);
        Classify(Options(), State(), Known()).Activity.Should().Be(ItsmWasasActivityClass.Unknown);
    }

    [Fact]
    public void Activity_SentBackAndSubmittedAgain_AnOpenRowIsPending_EvenWhenAnOlderOneWasApproved()
    {
        Classify(Options(), State(), Known(
            Row("1", created: "2026-01-05T01:00:00"), Row("2", completed: null, created: "2026-01-06T01:00:00")))
            .Activity.Should().Be(ItsmWasasActivityClass.Pending);
        // An open row wins even over a newer completed one: the action is still ours.
        Classify(Options(), State(), Known(
            Row("1", completed: null, created: "2026-01-05T01:00:00"), Row("2", created: "2026-01-06T01:00:00")))
            .Activity.Should().Be(ItsmWasasActivityClass.Pending);
    }

    [Fact]
    public void Activity_SeveralCompletedRows_TheNewestCreationTimeDecides()
    {
        // Rejected first, approved after resubmission.
        Classify(Options(), State(), Known(
            Row("1", status: "5", created: "2026-01-05T01:00:00"), Row("2", status: "4", created: "2026-01-06T01:00:00")))
            .Activity.Should().Be(ItsmWasasActivityClass.Completed);
        // Approved first, newest rejected: the approval is no longer the answer. Row order does not matter.
        Classify(Options(), State(), Known(
            Row("2", status: "5", created: "2026-01-06T01:00:00"), Row("1", status: "4", created: "2026-01-05T01:00:00")))
            .Activity.Should().Be(ItsmWasasActivityClass.NotApproved);
        // Time zones are compared as instants.
        Classify(Options(), State(), Known(
            Row("1", status: "5", created: "2026-01-05T10:00:00+03:00"), Row("2", status: "4", created: "2026-01-05T08:00:00+00:00")))
            .Activity.Should().Be(ItsmWasasActivityClass.Completed);
    }

    [Theory]
    [InlineData("2026-01-05T01:00:00", "2026-01-05T01:00:00")]
    [InlineData("2026-01-05T01:00:00", null)]
    [InlineData("2026-01-05T01:00:00", "not a date")]
    [InlineData(null, null)]
    public void Activity_SeveralCompletedRows_EqualOrUnreadableCreationTimes_AreUnknown(string? first, string? second)
    {
        ItsmWasasClassification result = Classify(Options(), State(), Known(
            Row("1", status: "4", created: first), Row("2", status: "5", created: second)));

        result.Activity.Should().Be(ItsmWasasActivityClass.Unknown);
    }

    [Theory]
    [InlineData(ItsmRecordLookupReason.IncompleteResponse)]
    [InlineData(ItsmRecordLookupReason.Timeout)]
    [InlineData(ItsmRecordLookupReason.InvalidResponse)]
    [InlineData(ItsmRecordLookupReason.Unavailable)]
    public void Activity_UnprovenPart_IsUnknownWithItsReason(ItsmRecordLookupReason reason)
    {
        ItsmWasasClassification result = Classify(Options(), State(), ItsmTrackingPart<IReadOnlyList<ItsmActivity>>.Unknown(reason));

        result.Activity.Should().Be(ItsmWasasActivityClass.Unknown);
        result.ActivityReason.Should().Be(reason);
    }

    [Theory]
    [InlineData("", "review task")]
    [InlineData(_wasasGroup, null)]
    public void Activity_WithoutAGroupOrTaskFilter_IsUnknownNotConfigured(string group, string? task)
    {
        ItsmWasasClassification result = Classify(
            Options(o => { o.WasasGroupId = group; o.ActivityTaskModelContains = task is null ? [] : [task]; }),
            State(), Known(Row()));

        result.Activity.Should().Be(ItsmWasasActivityClass.Unknown);
        result.ActivityReason.Should().Be(ItsmRecordLookupReason.NotConfigured);
    }

    [Fact]
    public void Activity_TaskFilter_IsCaseInsensitiveText_AndAMissingLabelNeverMatches()
    {
        Classify(Options(o => o.ActivityTaskModelContains = ["REVIEW TASK"]), State(), Known(Row())).Activity.Should().Be(ItsmWasasActivityClass.Completed);
        Classify(Options(), State(), Known(Row(label: null))).Activity.Should().Be(ItsmWasasActivityClass.Unknown);
    }

    [Theory]
    [InlineData("9100", "9002", ItsmOrClosureClass.Closed)]
    [InlineData("9001", "9200", ItsmOrClosureClass.Closed)]
    [InlineData("9001", "9002", ItsmOrClosureClass.Open)]
    [InlineData(null, "9002", ItsmOrClosureClass.Unknown)]
    [InlineData("9001", null, ItsmOrClosureClass.Unknown)]
    [InlineData("9100", null, ItsmOrClosureClass.Closed)]
    public void Closure_UsesTheConfiguredStageAndStatusIds(string? stage, string? status, ItsmOrClosureClass expected) =>
        Classify(Options(), State(stage, status), Known(Row())).Closure.Should().Be(expected);

    [Fact]
    public void Closure_WithoutConfiguredIds_IsUnknown_AndOnlyTheConfiguredListIsComparable()
    {
        Classify(Options(o => { o.ClosedStageIds = []; o.ClosedStatusIds = []; }), State("9100", "9200"), Known(Row())).Closure.Should().Be(ItsmOrClosureClass.Unknown);
        Classify(Options(o => o.ClosedStatusIds = []), State("9001", null), Known(Row())).Closure.Should().Be(ItsmOrClosureClass.Open);
        Classify(Options(o => o.ClosedStageIds = []), State(null, "9200"), Known(Row())).Closure.Should().Be(ItsmOrClosureClass.Closed);
    }

    [Theory]
    [InlineData(_wasasGroup, ItsmAssignmentClass.AssignedToWasas)]
    [InlineData(" " + _wasasGroup + " ", ItsmAssignmentClass.AssignedToWasas)]
    [InlineData(_otherGroup, ItsmAssignmentClass.NotAssignedToWasas)]
    [InlineData(null, ItsmAssignmentClass.Unknown)]
    [InlineData("", ItsmAssignmentClass.Unknown)]
    public void Assignment_ComparesTheFilledGroupWithTheConfiguredWasasGroup(string? group, ItsmAssignmentClass expected) =>
        Classify(Options(), State(group: group), Known(Row())).Assignment.Should().Be(expected);

    [Fact]
    public void Assignment_WithoutAWasasGroupOrGroupField_IsUnknown()
    {
        Classify(Options(o => o.WasasGroupId = ""), State(group: _otherGroup), Known(Row())).Assignment.Should().Be(ItsmAssignmentClass.Unknown);
        Classify(Options(o => o.OrAssignedGroupField = ""), State(group: _otherGroup), Known(Row())).Assignment.Should().Be(ItsmAssignmentClass.Unknown);
    }

    [Fact]
    public void ThreeClasses_AreIndependent_AGroupChangeIsNotProofOfApproval()
    {
        // Sent back: the activity row is completed with another code and the group changed. Not Completed.
        ItsmWasasClassification sentBack = Classify(Options(), State(group: _otherGroup), Known(Row(status: "5")));
        sentBack.Should().BeEquivalentTo(new ItsmWasasClassification(
            ItsmWasasActivityClass.NotApproved, ItsmOrClosureClass.Open, ItsmAssignmentClass.NotAssignedToWasas, ItsmRecordLookupReason.None, "Test"));

        // Group changed and the activity row was not readable: assignment is known, the activity is not.
        ItsmWasasClassification unread = Classify(Options(), State(group: _otherGroup), ItsmTrackingPart<IReadOnlyList<ItsmActivity>>.Unknown(ItsmRecordLookupReason.Timeout));
        unread.Activity.Should().Be(ItsmWasasActivityClass.Unknown);
        unread.Assignment.Should().Be(ItsmAssignmentClass.NotAssignedToWasas);
    }

    // ----- the Turuncu Hat adapter -----

    private static readonly (string Key, string? Value)[] _header =
    [
        .. OrHeaderRow.Where(cell => cell.Key != "num"),
        ("SET.p_synthetic_status", "9002"), ("KEY.p_synthetic_status", "Synthetic status"),
        ("SET.p_synthetic_stage", "9001"), ("KEY.p_synthetic_stage", "Synthetic stage"),
        ("SET.p_synthetic_group", _wasasGroup), ("KEY.p_synthetic_group", "Synthetic Group Name"),
        ("num", "1")
    ];

    private static (string Key, string? Value)[] ActivityRow(string id = "1", string group = _wasasGroup, string? status = "4",
        string? completed = "2026-01-05T01:45:00", string? label = _review, string? created = "2026-01-05T01:00:00") =>
    [
        ("SET.id", id), ("SET.m_group", group), ("KEY.m_group", "Synthetic Group Name"),
        ("SET.m_status", status), ("KEY.m_status", "Synthetic status text"),
        ("SET.m_created_dt", created), ("SET.m_completion_date", completed),
        ("SET.m_actvty_task_model", "103"), ("KEY.m_actvty_task_model", label),
        ("KEY.m_done_by", "Synthetic Person"), ("KEY.m_assignee", "Synthetic Person"), ("KEY.m_done_by_delegate", "Synthetic Person"),
        ("num", "1")
    ];

    private static TuruncuHatItsmRecordTracking Tracking(
        HttpMessageHandler handler,
        ITuruncuHatSessionManager? sessions = null,
        Action<ItsmRecordLookupOptions>? change = null,
        int requestTimeoutSeconds = 30,
        Microsoft.Extensions.Logging.ILogger<TuruncuHatItsmRecordTracking>? logger = null) =>
        new(Client(handler), sessions ?? Sessions(),
            Microsoft.Extensions.Options.Options.Create(new TuruncuHatOptions
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
            Microsoft.Extensions.Options.Options.Create(Options(change)),
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

    private static string[] Strings(JsonElement req, string property) =>
        [.. req.GetProperty(property).EnumerateArray().Select(element => element.GetString()!)];

    private static Task<ItsmWasasClassification> Run(ScriptedHandler handler, Action<ItsmRecordLookupOptions>? change = null) =>
        Tracking(handler, change: change).ClassifyAsync(OrSourceId, CancellationToken.None);

    [Theory]
    [InlineData("4", "2026-01-05T01:45:00", ItsmWasasActivityClass.Completed)]
    [InlineData("4", null, ItsmWasasActivityClass.Pending)]
    [InlineData("5", "2026-01-05T01:45:00", ItsmWasasActivityClass.NotApproved)]
    public async Task Adapter_ClassifiesTheFiveClasses_FromTwoSelectOnlyQueries(string? status, string? completed, ItsmWasasActivityClass expected)
    {
        ScriptedHandler handler = new(Ok(Query(_header)), Ok(Query(ActivityRow(status: status, completed: completed))));

        ItsmWasasClassification result = await Run(handler);

        result.Activity.Should().Be(expected);
        result.Closure.Should().Be(ItsmOrClosureClass.Open);
        result.Assignment.Should().Be(ItsmAssignmentClass.AssignedToWasas);
        result.Provider.Should().Be("TuruncuHat");
        // Header and activity only: the comment query is not part of the classification.
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Adapter_ClosedOr_AndOtherGroup_AreReported_WithAnEmptyGroupOnTheClosedRowUnknown()
    {
        ScriptedHandler other = new(
            Ok(Query(_header.With("SET.p_synthetic_group", _otherGroup))), Ok(Query(ActivityRow())));
        (await Run(other)).Assignment.Should().Be(ItsmAssignmentClass.NotAssignedToWasas);

        ScriptedHandler closed = new(
            Ok(Query(_header.With("SET.p_synthetic_stage", "9100").With("SET.p_synthetic_status", "9200").With("SET.p_synthetic_group", null))),
            Ok(Query(ActivityRow())));
        ItsmWasasClassification result = await Run(closed);
        result.Closure.Should().Be(ItsmOrClosureClass.Closed);
        result.Assignment.Should().Be(ItsmAssignmentClass.Unknown);
        result.Activity.Should().Be(ItsmWasasActivityClass.Completed);
    }

    [Fact]
    public async Task Adapter_Requests_SelectConfiguredFieldsOnly_NeverAPersonField()
    {
        ScriptedHandler handler = new(Ok(Query(_header)), Ok(Query(ActivityRow())));

        await Run(handler);

        Strings(Req(handler, 0), "Selects").Should().Equal("id", "m_last_mod_dt", "p_synthetic_status", "p_synthetic_stage", "p_synthetic_group");
        Strings(Req(handler, 1), "Selects").Should().Equal("id", "m_group", "m_status", "m_created_dt", "m_completion_date", "m_actvty_task_model");
        foreach ((string _, string body) in handler.Requests)
        {
            body.Should().NotContainAny("m_done_by", "m_assignee", "m_done_by_delegate", "m_comment", "Password", "Username");
            JsonDocument.Parse(body).RootElement.GetProperty("req").TryGetProperty("QueryPermissions", out _).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Adapter_WithoutTaskFilterOrGroupField_SelectsNeitherAndStaysUnknown()
    {
        ScriptedHandler handler = new(Ok(Query(_header)), Ok(Query(ActivityRow())));

        ItsmWasasClassification result = await Run(handler, o => { o.ActivityTaskModelContains = []; o.OrAssignedGroupField = ""; });

        Strings(Req(handler, 0), "Selects").Should().Equal("id", "m_last_mod_dt", "p_synthetic_status", "p_synthetic_stage");
        Strings(Req(handler, 1), "Selects").Should().Equal("id", "m_group", "m_status", "m_created_dt", "m_completion_date");
        result.Activity.Should().Be(ItsmWasasActivityClass.Unknown);
        result.ActivityReason.Should().Be(ItsmRecordLookupReason.NotConfigured);
        result.Assignment.Should().Be(ItsmAssignmentClass.Unknown);
    }

    [Fact]
    public async Task Adapter_PersonFieldsInTheResponse_NeverLeakIntoTheResult()
    {
        ScriptedHandler handler = new(Ok(Query(_header)), Ok(Query(ActivityRow())));
        ItsmWasasClassification result = await Run(handler);

        JsonSerializer.Serialize(result).Should().NotContainAny("Synthetic Person", "Synthetic Group Name", "Synthetic status", OrSourceId, _wasasGroup);
    }

    [Fact]
    public async Task Adapter_HtmlEncodedTaskLabel_IsDecodedBeforeMatching()
    {
        ScriptedHandler handler = new(Ok(Query(_header)), Ok(Query(ActivityRow(label: "Synthetic &amp; review task &lt;1&gt;"))));

        (await Run(handler, o => o.ActivityTaskModelContains = ["& review task <1>"])).Activity.Should().Be(ItsmWasasActivityClass.Completed);
    }

    [Fact]
    public async Task Adapter_OversizedTaskLabel_IsUnknownInvalidResponse_NotTruncated()
    {
        ScriptedHandler handler = new(Ok(Query(_header)), Ok(Query(ActivityRow(label: new string('x', 257)))));

        ItsmWasasClassification result = await Run(handler);

        result.Activity.Should().Be(ItsmWasasActivityClass.Unknown);
        result.ActivityReason.Should().Be(ItsmRecordLookupReason.InvalidResponse);
    }

    [Fact]
    public async Task Adapter_SeveralMatchingRows_FollowTheResubmissionRule()
    {
        (await Run(new ScriptedHandler(Ok(Query(_header)), Ok(Query(ActivityRow("1"), ActivityRow("2", completed: null, created: "2026-01-06T01:00:00"))))))
            .Activity.Should().Be(ItsmWasasActivityClass.Pending);
        (await Run(new ScriptedHandler(Ok(Query(_header)), Ok(Query(
            ActivityRow("1", status: "5"), ActivityRow("2", status: "4", created: "2026-01-06T01:00:00"))))))
            .Activity.Should().Be(ItsmWasasActivityClass.Completed);
        (await Run(new ScriptedHandler(Ok(Query(_header)), Ok(Query(ActivityRow("1"), ActivityRow("2", status: "5"))))))
            .Activity.Should().Be(ItsmWasasActivityClass.Unknown);
    }

    [Fact]
    public async Task Adapter_RowMissingTheStatusCell_IsMalformed_AndTheActivityUnknown()
    {
        ScriptedHandler handler = new(Ok(Query(_header)), Ok(Query(ActivityRow().Where(cell => cell.Key != "SET.m_status").ToArray())));

        ItsmWasasClassification result = await Run(handler);

        result.Activity.Should().Be(ItsmWasasActivityClass.Unknown);
        result.Closure.Should().Be(ItsmOrClosureClass.Open);
    }

    [Fact]
    public async Task Adapter_PartialPage_IsUnknown_NeverCompleted()
    {
        ScriptedHandler handler = new(Ok(Query(_header)), Ok(Envelope([ActivityRow()], maxPages: 2, recordCount: 80)));

        ItsmWasasClassification result = await Run(handler);

        result.Activity.Should().Be(ItsmWasasActivityClass.Unknown);
        result.ActivityReason.Should().Be(ItsmRecordLookupReason.IncompleteResponse);
        result.Closure.Should().Be(ItsmOrClosureClass.Open);
    }

    [Fact]
    public async Task Adapter_MoreThanTheActivityBound_IsIncomplete()
    {
        (string Key, string? Value)[][] rows = [.. Enumerable.Range(1, TuruncuHatItsmRecordTracking.MaxActivities + 1).Select(i => ActivityRow(i.ToString()))];
        ScriptedHandler handler = new(Ok(Query(_header)), Ok(Query(rows)));

        (await Run(handler)).Activity.Should().Be(ItsmWasasActivityClass.Unknown);
    }

    [Fact]
    public async Task Adapter_OrNotFoundOrAmbiguous_IsEntirelyUnknown_NeverClosedOrAbsent()
    {
        ItsmWasasClassification notFound = await Run(new ScriptedHandler(Ok(Query())));
        ItsmWasasClassification ambiguous = await Run(new ScriptedHandler(Ok(Query(_header, _header))));

        foreach (ItsmWasasClassification result in new[] { notFound, ambiguous })
        {
            result.Activity.Should().Be(ItsmWasasActivityClass.Unknown);
            result.Closure.Should().Be(ItsmOrClosureClass.Unknown);
            result.Assignment.Should().Be(ItsmAssignmentClass.Unknown);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, ItsmRecordLookupReason.Unavailable)]
    [InlineData(HttpStatusCode.BadRequest, ItsmRecordLookupReason.Rejected)]
    public async Task Adapter_FailedHeader_IsUnknownWithTheTransportReason(HttpStatusCode status, ItsmRecordLookupReason reason)
    {
        ItsmWasasClassification result = await Run(new ScriptedHandler(Response(status, "{}")));

        result.Should().Be(ItsmWasasClassification.Unknown(reason, "TuruncuHat"));
    }

    [Fact]
    public async Task Adapter_FailedActivityRead_KeepsTheHeaderClassesAndMarksTheActivityUnknown()
    {
        ItsmWasasClassification result = await Run(new ScriptedHandler(Ok(Query(_header)), Response(HttpStatusCode.InternalServerError, "{}")));

        result.Activity.Should().Be(ItsmWasasActivityClass.Unknown);
        result.ActivityReason.Should().Be(ItsmRecordLookupReason.Unavailable);
        result.Assignment.Should().Be(ItsmAssignmentClass.AssignedToWasas);
    }

    [Fact]
    public async Task Adapter_Timeout_IsUnknownTimeout()
    {
        ItsmWasasClassification result = await Tracking(new HangingHandler(), requestTimeoutSeconds: 1)
            .ClassifyAsync(OrSourceId, CancellationToken.None);

        result.Should().Be(ItsmWasasClassification.Unknown(ItsmRecordLookupReason.Timeout, "TuruncuHat"));
    }

    [Fact]
    public async Task Adapter_CallerCancellation_Propagates()
    {
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Func<Task> act = () => Tracking(new ScriptedHandler()).ClassifyAsync(OrSourceId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Adapter_ExpiredSession_IsRefreshedOnceAndTheClassificationCompletes()
    {
        ITuruncuHatSessionManager sessions = Sessions();
        ScriptedHandler handler = new(Response(HttpStatusCode.Unauthorized, "{}"), Ok(Query(_header)), Ok(Query(ActivityRow())));

        ItsmWasasClassification result = await Tracking(handler, sessions).ClassifyAsync(OrSourceId, CancellationToken.None);

        result.Activity.Should().Be(ItsmWasasActivityClass.Completed);
        sessions.Received(1).Invalidate(_session);
        handler.Requests.Should().HaveCount(3);
    }

    [Theory]
    [InlineData("1 OR 1=1")]
    [InlineData("0")]
    [InlineData(null)]
    public async Task Adapter_InvalidSourceId_IsUnknownInvalidIdentifier_WithoutARequest(string? id)
    {
        ScriptedHandler handler = new();

        ItsmWasasClassification result = await Tracking(handler).ClassifyAsync(id, CancellationToken.None);

        result.Should().Be(ItsmWasasClassification.Unknown(ItsmRecordLookupReason.InvalidIdentifier, "TuruncuHat"));
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Adapter_Logs_NeverCarryUserSessionUrlPersonOrValues()
    {
        CapturingLogger<TuruncuHatItsmRecordTracking> logger = new();
        ScriptedHandler handler = new(
            Response(HttpStatusCode.InternalServerError, "{}"),
            Ok(Query(_header)), Ok(Query(ActivityRow("1"), ActivityRow("2"))));
        TuruncuHatItsmRecordTracking tracking = Tracking(handler, logger: logger);

        await tracking.ClassifyAsync(OrSourceId, CancellationToken.None);
        await tracking.ClassifyAsync(OrSourceId, CancellationToken.None);

        logger.Messages.Should().NotBeEmpty();
        string all = string.Join('\n', logger.Messages);
        all.Should().NotContainAny(_session, "synthetic-user", "synthetic-password", "Basic c3ludGhldGlj", "itsm.invalid",
            OrSourceId, _wasasGroup, "Synthetic Person", "Synthetic Group Name", _review);
    }

    // ----- Disabled, Fake, registration -----

    [Fact]
    public async Task Disabled_IsUnknownNotConfigured_AndNeverClosedOrCompleted()
    {
        ItsmWasasClassification result = await new DisabledItsmRecordTracking().ClassifyAsync(OrSourceId, CancellationToken.None);

        result.Should().Be(ItsmWasasClassification.Unknown(ItsmRecordLookupReason.NotConfigured, "Disabled"));
    }

    [Theory]
    [InlineData("900001", ItsmWasasActivityClass.Pending, ItsmOrClosureClass.Open, ItsmAssignmentClass.AssignedToWasas)]
    [InlineData("900101", ItsmWasasActivityClass.Completed, ItsmOrClosureClass.Open, ItsmAssignmentClass.NotAssignedToWasas)]
    [InlineData("900102", ItsmWasasActivityClass.NotApproved, ItsmOrClosureClass.Open, ItsmAssignmentClass.NotAssignedToWasas)]
    [InlineData("900103", ItsmWasasActivityClass.Completed, ItsmOrClosureClass.Closed, ItsmAssignmentClass.Unknown)]
    [InlineData("900002", ItsmWasasActivityClass.Unknown, ItsmOrClosureClass.Unknown, ItsmAssignmentClass.Unknown)]
    [InlineData("424242", ItsmWasasActivityClass.Unknown, ItsmOrClosureClass.Unknown, ItsmAssignmentClass.Unknown)]
    public async Task Fake_IsDeterministic_AndSyntheticOnly(string id, ItsmWasasActivityClass activity, ItsmOrClosureClass closure, ItsmAssignmentClass assignment)
    {
        ItsmWasasClassification result = await new FakeItsmRecordTracking().ClassifyAsync(id, CancellationToken.None);

        (result.Activity, result.Closure, result.Assignment, result.Provider).Should().Be((activity, closure, assignment, "Fake"));
    }

    [Fact]
    public async Task Fake_UnavailableAndInvalidIds_AreUnknownWithTheirReasons()
    {
        FakeItsmRecordTracking fake = new();

        (await fake.ClassifyAsync("900003", CancellationToken.None)).ActivityReason.Should().Be(ItsmRecordLookupReason.Unavailable);
        (await fake.ClassifyAsync("bad", CancellationToken.None)).ActivityReason.Should().Be(ItsmRecordLookupReason.InvalidIdentifier);
    }

    [Fact]
    public void Registration_Default_IsDisabled_AndNeverFallsBackToFake()
    {
        using ServiceProvider provider = Provider(new());

        provider.GetRequiredService<IItsmWasasTracking>().Should().BeOfType<DisabledItsmRecordTracking>();
        provider.GetRequiredService<IItsmRecordTracking>().Should().BeSameAs(provider.GetRequiredService<IItsmWasasTracking>());
    }

    [Fact]
    public void Registration_TuruncuHat_ResolvesTheRealAdapter_ForBothInterfaces()
    {
        Dictionary<string, string?> settings = TuruncuHatSettings();
        settings["ItsmRecordLookup:WasasGroupId"] = _wasasGroup;
        settings["ItsmRecordLookup:ClosedStageIds:0"] = "9100";
        settings["ItsmRecordLookup:ActivityTaskModelContains:0"] = "review task";

        using ServiceProvider provider = Provider(settings);

        provider.GetRequiredService<IItsmWasasTracking>().Should().BeOfType<TuruncuHatItsmRecordTracking>();
        provider.GetRequiredService<IItsmRecordTracking>().Should().BeSameAs(provider.GetRequiredService<IItsmWasasTracking>());
        provider.GetRequiredService<IOptions<ItsmRecordLookupOptions>>().Value.ClosedStageIds.Should().Equal("9100");
        provider.GetRequiredService<IOptions<ItsmRecordLookupOptions>>().Value.ActivityApprovedStatus.Should().Be("4");
    }

    [Theory]
    [InlineData("ItsmRecordLookup:WasasGroupId", "group-x")]
    [InlineData("ItsmRecordLookup:ActivityApprovedStatus", "approved")]
    [InlineData("ItsmRecordLookup:ClosedStageIds:0", "not-a-number")]
    [InlineData("ItsmRecordLookup:ClosedStatusIds:0", "")]
    [InlineData("ItsmRecordLookup:ActivityTaskModelContains:0", "  ")]
    [InlineData("ItsmRecordLookup:OrAssignedGroupField", "bad name")]
    public void Validation_RejectsMalformedClassificationSettings_AtStartup(string key, string value)
    {
        Dictionary<string, string?> settings = TuruncuHatSettings();
        settings[key] = value;

        Action act = () => ItsmRecordLookupRegistration.Validate(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), "Production");

        act.Should().Throw<InvalidOperationException>();
    }

    private static ServiceProvider Provider(Dictionary<string, string?> settings)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSecureOpsInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), "Production");
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> TuruncuHatSettings() => new()
    {
        ["ItsmRecordLookup:Provider"] = "TuruncuHat",
        ["ItsmRecordLookup:OrBaseObject"] = "SyntheticRequest",
        ["ItsmRecordLookup:OcoBaseObject"] = "SyntheticChange",
        ["TuruncuHat:BaseUrl"] = "https://itsm.invalid/api/",
        ["TuruncuHat:Authorization"] = "Basic c3ludGhldGljOnN5bnRoZXRpYw==",
        ["TuruncuHat:Username"] = "synthetic-user",
        ["TuruncuHat:Password"] = "synthetic-password",
        ["TuruncuHat:TenantId"] = "7",
        ["TuruncuHat:SessionLifetimeSeconds"] = "600"
    };

    // ----- the In Use bridge -----

    private static InUseSource BareSource() => new("700456", "OR-SYNTHETIC", "Synthetic", new("Synthetic requester", "fixture"),
        new(null, "fixture"), new(null, "fixture"), [], "", true);

    [Fact]
    public void Bridge_Apply_FillsOnlyProvenClasses_WithProvenance()
    {
        InUseSource source = TrackingClassifiedInUseSourceClient.Apply(BareSource(), new(
            ItsmWasasActivityClass.Completed, ItsmOrClosureClass.Open, ItsmAssignmentClass.NotAssignedToWasas, ItsmRecordLookupReason.None, "TuruncuHat"));

        source.WasasActivity!.Value.Should().Be("Completed");
        source.Lifecycle!.Value.Should().Be("Open");
        source.CurrentStage!.Value.Should().Be("NotAssignedToWasas");
        new[] { source.WasasActivity, source.Lifecycle, source.CurrentStage }.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e!.Source));
        // The provenance names the rule, never a record id, group id, code or person.
        JsonSerializer.Serialize(source).Should().NotContain(_wasasGroup);
    }

    [Fact]
    public void Bridge_Apply_Unknown_LeavesTheSourceUntouched()
    {
        InUseSource original = BareSource() with { CurrentStage = new("Existing", "fixture") };

        TrackingClassifiedInUseSourceClient.Apply(original, ItsmWasasClassification.Unknown(ItsmRecordLookupReason.Timeout, "TuruncuHat"))
            .Should().Be(original);
    }

    [Theory]
    [InlineData(ItsmWasasActivityClass.Pending, "Pending")]
    [InlineData(ItsmWasasActivityClass.NotApproved, "NotApproved")]
    public void Bridge_Apply_MapsActivityClasses(ItsmWasasActivityClass activity, string expected) =>
        TrackingClassifiedInUseSourceClient.Apply(BareSource(), new(activity, ItsmOrClosureClass.Unknown, ItsmAssignmentClass.Unknown, ItsmRecordLookupReason.None, "TuruncuHat"))
            .WasasActivity!.Value.Should().Be(expected);

    private static InUseRecord Record(InUseSource source) =>
        new(Guid.NewGuid(), source, "hash", 1, 1, null, null, null, DateTimeOffset.UtcNow);

    [Fact]
    public void Drop_NotAssigned_LeavesTheMainList_AsTrackedOnly()
    {
        InUseRecord left = Record(BareSource() with { CurrentStage = new(InUseActivity.NotAssignedToWasas, "rule") });

        left.ActivityStatus.Should().Be("NotAssigned");
        left.TrackingOnly.Should().BeTrue();
    }

    [Fact]
    public void Drop_Unknown_Assigned_AndUnprovenStages_StayInTheMainList()
    {
        // Unknown (null evidence), an assigned record, and a value without provenance never drop a record.
        Record(BareSource()).TrackingOnly.Should().BeFalse();
        Record(BareSource() with { CurrentStage = new("AssignedToWasas", "rule") }).TrackingOnly.Should().BeFalse();
        Record(BareSource() with { CurrentStage = new(InUseActivity.NotAssignedToWasas, " ") }).TrackingOnly.Should().BeFalse();
    }

    [Fact]
    public void Drop_Precedence_ProductApproval_And_OrClosed_ComeBeforeNotAssigned()
    {
        InUseSource moved = BareSource() with { CurrentStage = new(InUseActivity.NotAssignedToWasas, "rule") };

        (Record(moved) with { ActivityVerifiedAt = DateTimeOffset.UtcNow }).ActivityStatus.Should().Be("Completed");
        Record(moved with { WasasActivity = new("Completed", "rule") }).ActivityStatus.Should().Be("Completed");
        Record(moved with { Lifecycle = new("Closed", "rule") }).ActivityStatus.Should().Be("OrClosed");
        // A pending activity row next to a group that moved is still not a review candidate.
        Record(moved with { WasasActivity = new("Pending", "rule") }).ActivityStatus.Should().Be("NotAssigned");
        // An OR that merely looks open with an unknown activity stays a verification candidate.
        Record(BareSource() with { Lifecycle = new("Open", "rule") }).ActivityStatus.Should().Be("VerificationPending");
    }

    [Fact]
    public async Task Bridge_Client_ClassifiesEveryRecord_AndKeepsTheBatchMeta()
    {
        IInUseSourceClient inner = Substitute.For<IInUseSourceClient>();
        inner.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(new InUseBatch([BareSource(), BareSource() with { Id = "900101" }], false, "Issue"));

        InUseBatch batch = await new TrackingClassifiedInUseSourceClient(inner, new FakeItsmRecordTracking()).DiscoverAsync(CancellationToken.None);

        batch.Complete.Should().BeFalse();
        batch.Issue.Should().Be("Issue");
        // Id 700456 is unknown to the Fake (Unknown, nothing filled); 900101 is a moved, completed record.
        batch.Records[0].CurrentStage.Should().BeNull();
        batch.Records[1].CurrentStage!.Value.Should().Be("NotAssignedToWasas");
        batch.Records[1].WasasActivity!.Value.Should().Be("Completed");
        Record(batch.Records[1]).ActivityStatus.Should().Be("Completed");
    }
}
