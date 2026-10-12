using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SecureOps.Infrastructure.ItsmRecords;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;
using static SecureOps.Tests.Unit.ItsmRecords.TuruncuHatItsmRecordFixtures;

namespace SecureOps.Tests.Unit.ItsmRecords;

public sealed class TuruncuHatItsmRecordLookupTests
{
    private const string _session = "synthetic-session";
    private const string _baseUrl = "https://itsm.invalid/api/";

    [Fact]
    public async Task Oco_SingleKeyedRow_IsFound_WithDecodedTitleAndRawCodes()
    {
        ScriptedHandler handler = new(Ok(Query(OcoRow)));

        ItsmRecordLookupResult result = await Lookup(handler).LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Found);
        result.Reason.Should().Be(ItsmRecordLookupReason.None);
        result.Provider.Should().Be("TuruncuHat");
        result.Number.Should().Be(OcoNumber);
        result.Record.Should().Be(new ItsmRecord("700123", OcoNumber, "Synthetic change & window <test>",
            "8101", "8201", "8301", "2026-01-05T01:00:00", "2026-01-05T03:00:00"));
    }

    [Fact]
    public async Task Oco_Request_UsesAllowListedSelects_ExactFilter_AndNoPersonField()
    {
        ScriptedHandler handler = new(Ok(Query(OcoRow)));

        await Lookup(handler).LookupAsync(ItsmRecordKind.Oco, " oco-000123 ", CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Path.Should().Be("/api/query");
        JsonElement req = JsonDocument.Parse(handler.Requests[0].Body).RootElement.GetProperty("req");
        req.GetProperty("BaseObject").GetString().Should().Be("SyntheticChange");
        req.GetProperty("Filters").EnumerateArray().Select(f => f.GetString()).Should()
            .Equal("#%m_active%#='True' AND #%p_code%#='OCO-000123'");
        req.GetProperty("Selects").EnumerateArray().Select(s => s.GetString()).Should().Equal(
            "id", "p_code", "p_name", "p_def_status", "p_rel_stage", "p_rel_group",
            "p_proposed_start_date_time", "p_proposed_finish_date_time");
        req.GetProperty("SessionID").GetString().Should().Be(_session);
        req.GetProperty("TenantId").GetInt32().Should().Be(7);
        req.TryGetProperty("NoEncode", out _).Should().BeFalse();
        handler.Requests[0].Body.Should().NotContainAny("requester", "affected_end_user", "Password", "Username");
    }

    [Fact]
    public async Task Or_SingleRow_IsFound_WithoutStatusStageOrWindow()
    {
        ScriptedHandler handler = new(Ok(Query(OrRow)));

        ItsmRecordLookupResult result = await Lookup(handler).LookupAsync(ItsmRecordKind.Or, OrNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Found);
        result.Record.Should().Be(new ItsmRecord("700456", OrNumber, "Synthetic request", null, null, "8301", null, null));
        JsonElement req = JsonDocument.Parse(handler.Requests[0].Body).RootElement.GetProperty("req");
        req.GetProperty("BaseObject").GetString().Should().Be("SyntheticRequest");
        req.GetProperty("Selects").EnumerateArray().Select(s => s.GetString()).Should().Equal("id", "p_code", "p_name", "p_rel_group");
    }

    [Fact]
    public async Task CompleteResponseWithZeroRows_IsNotFound()
    {
        ItsmRecordLookupResult result = await Lookup(new ScriptedHandler(Ok(Query())))
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.NotFound);
        result.Reason.Should().Be(ItsmRecordLookupReason.None);
        result.Record.Should().BeNull();
    }

    [Fact]
    public async Task DuplicateRows_AreAmbiguous_AndNoRecordIsChosen()
    {
        ItsmRecordLookupResult result = await Lookup(new ScriptedHandler(Ok(Query(OcoRow, OcoRow.With("SET.id", "700124")))))
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Ambiguous);
        result.Record.Should().BeNull();
    }

    [Fact]
    public async Task IdenticalDuplicateRows_AreStillAmbiguous()
    {
        ItsmRecordLookupResult result = await Lookup(new ScriptedHandler(Ok(Query(OcoRow, OcoRow))))
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Ambiguous);
    }

    [Theory]
    [InlineData("SET.p_rel_stage")]
    [InlineData("SET.p_name")]
    [InlineData("SET.id")]
    public async Task MissingSelectedKey_IsUnknownIncomplete(string missing)
    {
        ItsmRecordLookupResult result = await Lookup(new ScriptedHandler(Ok(Query(OcoRow.Without(missing)))))
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.IncompleteResponse);
    }

    [Fact]
    public async Task OneMalformedRowBesideAValidRow_IsUnknown_NotFound()
    {
        ItsmRecordLookupResult result = await Lookup(new ScriptedHandler(Ok(Query(OcoRow, OcoRow.Without("SET.p_code")))))
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.IncompleteResponse);
    }

    [Fact]
    public async Task EmptySourceId_IsUnknownIncomplete()
    {
        ItsmRecordLookupResult result = await Lookup(new ScriptedHandler(Ok(Query(OcoRow.With("SET.id", " ")))))
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Reason.Should().Be(ItsmRecordLookupReason.IncompleteResponse);
    }

    [Fact]
    public async Task ConflictingDuplicateKeyInOneRow_IsUnknownIncomplete()
    {
        (string, string?)[] row = [.. OcoRow, ("SET.p_def_status", "9999")];

        ItsmRecordLookupResult result = await Lookup(new ScriptedHandler(Ok(Query(row))))
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Reason.Should().Be(ItsmRecordLookupReason.IncompleteResponse);
    }

    [Fact]
    public async Task PositionalCells_AreNeverMappedByPosition()
    {
        ItsmRecordLookupResult result = await Lookup(new ScriptedHandler(Ok(PositionalRow)))
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.IncompleteResponse);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 2)]
    public async Task MorePagesOrRecordsThanReturned_IsUnknownIncomplete_EvenWithZeroItems(int maxPages, int recordCount)
    {
        ItsmRecordLookupResult result = await Lookup(new ScriptedHandler(Ok(Envelope([], maxPages, recordCount))))
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.IncompleteResponse);
    }

    [Fact]
    public async Task RowForAnotherNumber_IsUnknownInvalidResponse()
    {
        ItsmRecordLookupResult result = await Lookup(new ScriptedHandler(Ok(Query(OcoRow.With("SET.p_code", "OCO-000999")))))
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.InvalidResponse);
    }

    [Fact]
    public async Task ApplicationErrorEnvelope_IsUnknownRejected()
    {
        ItsmRecordLookupResult result = await Lookup(new ScriptedHandler(Ok(ApplicationError)))
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Reason.Should().Be(ItsmRecordLookupReason.Rejected);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"QueryResult":{"ErrorNo":0}}""")]
    [InlineData("""{"QueryResult":{"ErrorNo":"x","Items":[]}}""")]
    public async Task UnreadableEnvelope_IsUnknownInvalidResponse(string body)
    {
        ItsmRecordLookupResult result = await Lookup(new ScriptedHandler(Ok(body)))
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.InvalidResponse);
    }

    [Fact]
    public async Task OversizedResponse_IsUnknownInvalidResponse()
    {
        string body = Query(OcoRow.With("SET.p_name", new string('x', 3000)));

        ItsmRecordLookupResult result = await Lookup(new ScriptedHandler(Ok(body)), maxResponseBytes: 1024)
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Reason.Should().Be(ItsmRecordLookupReason.InvalidResponse);
    }

    [Fact]
    public async Task TitleOverLimit_IsUnknownIncomplete()
    {
        ItsmRecordLookupResult result = await Lookup(new ScriptedHandler(Ok(Query(OcoRow.With("SET.p_name", new string('x', 501))))))
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Reason.Should().Be(ItsmRecordLookupReason.IncompleteResponse);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, ItsmRecordLookupReason.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, ItsmRecordLookupReason.Unavailable)]
    [InlineData(HttpStatusCode.BadRequest, ItsmRecordLookupReason.Rejected)]
    [InlineData(HttpStatusCode.NotFound, ItsmRecordLookupReason.Rejected)]
    public async Task HttpFailure_IsUnknown_NeverNotFound(HttpStatusCode status, ItsmRecordLookupReason reason)
    {
        ItsmRecordLookupResult result = await Lookup(new ScriptedHandler(Response(status, "{}")))
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Unknown);
        result.Reason.Should().Be(reason);
    }

    [Fact]
    public async Task TransportFailure_IsUnknownUnavailable()
    {
        ItsmRecordLookupResult result = await Lookup(new ThrowingHandler(new HttpRequestException("synthetic")))
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Reason.Should().Be(ItsmRecordLookupReason.Unavailable);
    }

    [Fact]
    public async Task DependencyTimeout_IsUnknownTimeout()
    {
        ItsmRecordLookupResult result = await Lookup(new HangingHandler(), requestTimeoutSeconds: 1)
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.Timeout);
    }

    [Fact]
    public async Task CallerCancellation_Propagates_BeforeAnyRequest()
    {
        ScriptedHandler handler = new(Ok(Query(OcoRow)));
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        Func<Task> act = () => Lookup(handler).LookupAsync(ItsmRecordKind.Oco, OcoNumber, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CallerCancellation_DuringRequest_Propagates_NotTimeout()
    {
        using CancellationTokenSource caller = new();
        HangingHandler handler = new(onSend: caller.Cancel);

        Func<Task> act = () => Lookup(handler, requestTimeoutSeconds: 30).LookupAsync(ItsmRecordKind.Oco, OcoNumber, caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExpiredSession_PerformsOneControlledRelogin_WithTheNewSession()
    {
        ScriptedHandler handler = new(
            Ok("""{"LoginResult":"ok|session-one"}"""),
            Response(HttpStatusCode.Unauthorized, "{}"),
            Ok("""{"LoginResult":"ok|session-two"}"""),
            Ok(Query(OcoRow)));
        TuruncuHatItsmRecordLookup lookup = LookupWithRealSessions(handler);

        ItsmRecordLookupResult result = await lookup.LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Found);
        handler.Requests.Select(r => r.Path).Should().Equal("/api/login", "/api/query", "/api/login", "/api/query");
        JsonDocument.Parse(handler.Requests[1].Body).RootElement.GetProperty("req").GetProperty("SessionID").GetString()
            .Should().Be("ok|session-one");
        JsonDocument.Parse(handler.Requests[3].Body).RootElement.GetProperty("req").GetProperty("SessionID").GetString()
            .Should().Be("ok|session-two");
    }

    [Fact]
    public async Task SessionRejectedAgainAfterRelogin_IsUnknownAuthenticationFailed_WithoutAThirdQuery()
    {
        ScriptedHandler handler = new(
            Ok("""{"LoginResult":"ok|session-one"}"""),
            Response(HttpStatusCode.Forbidden, "{}"),
            Ok("""{"LoginResult":"ok|session-two"}"""),
            Response(HttpStatusCode.Unauthorized, "{}"));

        ItsmRecordLookupResult result = await LookupWithRealSessions(handler)
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.AuthenticationFailed);
        handler.Requests.Should().HaveCount(4);
    }

    [Theory]
    [InlineData(OperationalErrorCodes.OperationalSourceAuthenticationFailed, ItsmRecordLookupReason.AuthenticationFailed)]
    [InlineData(OperationalErrorCodes.OperationalSourceUnavailable, ItsmRecordLookupReason.Unavailable)]
    public async Task LoginFailure_IsUnknown_WithoutAQuery(string errorCode, ItsmRecordLookupReason reason)
    {
        ITuruncuHatSessionManager sessions = Substitute.For<ITuruncuHatSessionManager>();
        sessions.GetSessionAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new ExternalIntegrationException(errorCode, false));
        ScriptedHandler handler = new();

        ItsmRecordLookupResult result = await Lookup(handler, sessions: sessions)
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Reason.Should().Be(reason);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task FailedLoginResultSegment_IsUnknownAuthenticationFailed()
    {
        ScriptedHandler handler = new(Ok("""{"LoginResult":"1|0"}"""));

        ItsmRecordLookupResult result = await LookupWithRealSessions(handler)
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Reason.Should().Be(ItsmRecordLookupReason.AuthenticationFailed);
        handler.Requests.Select(r => r.Path).Should().Equal("/api/login");
    }

    [Theory]
    [InlineData(ItsmRecordKind.Oco, "OCO-000123' OR '1'='1")]
    [InlineData(ItsmRecordKind.Oco, "OCO-")]
    [InlineData(ItsmRecordKind.Oco, "OCO-12 3")]
    [InlineData(ItsmRecordKind.Oco, "OCO-%")]
    [InlineData(ItsmRecordKind.Oco, "OCO-#%p_code%#")]
    [InlineData(ItsmRecordKind.Oco, "OCO-١٢٣")]
    [InlineData(ItsmRecordKind.Oco, "OCO-123456789012345678901")]
    [InlineData(ItsmRecordKind.Oco, "OR-000123")]
    [InlineData(ItsmRecordKind.Or, "OCO-000123")]
    [InlineData(ItsmRecordKind.Or, "")]
    [InlineData(ItsmRecordKind.Or, null)]
    public async Task InvalidNumber_IsUnknownInvalidNumber_WithoutAnyRequest(ItsmRecordKind kind, string? number)
    {
        ScriptedHandler handler = new();
        ITuruncuHatSessionManager sessions = Sessions();

        ItsmRecordLookupResult result = await Lookup(handler, sessions: sessions).LookupAsync(kind, number, CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.InvalidNumber);
        result.Number.Should().BeNull();
        handler.Requests.Should().BeEmpty();
        await sessions.DidNotReceive().GetSessionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnconfiguredBaseObject_IsUnknownNotConfigured_WithoutAnyRequest()
    {
        ScriptedHandler handler = new();

        ItsmRecordLookupResult result = await Lookup(handler, ocoBaseObject: "")
            .LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);

        result.Reason.Should().Be(ItsmRecordLookupReason.NotConfigured);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Logs_NeverContainSessionCredentialsUrlOrNumber()
    {
        CapturingLogger logger = new();
        ScriptedHandler handler = new(
            Ok("""{"LoginResult":"ok|session-secret-one"}"""),
            Response(HttpStatusCode.Unauthorized, "{}"),
            Ok("""{"LoginResult":"ok|session-secret-two"}"""),
            Response(HttpStatusCode.InternalServerError, "{}"),
            Ok(ApplicationError),
            Ok("not json"),
            Ok(Query(OcoRow.Without("SET.p_rel_stage"))),
            Ok(Query(OcoRow)));
        TuruncuHatItsmRecordLookup lookup = LookupWithRealSessions(handler, logger);

        for (int i = 0; i < 5; i++)
        {
            await lookup.LookupAsync(ItsmRecordKind.Oco, OcoNumber, CancellationToken.None);
        }

        logger.Messages.Should().NotBeEmpty();
        string all = string.Join('\n', logger.Messages);
        all.Should().NotContainAny("session-secret", "synthetic-user", "synthetic-password", "Basic c3ludGhldGlj",
            "itsm.invalid", OcoNumber, "000123", "Synthetic change");
    }

    private static TuruncuHatItsmRecordLookup Lookup(
        HttpMessageHandler handler,
        ITuruncuHatSessionManager? sessions = null,
        int requestTimeoutSeconds = 30,
        int maxResponseBytes = 1_048_576,
        string ocoBaseObject = "SyntheticChange") =>
        new(Client(handler), sessions ?? Sessions(),
            Options.Create(Wire(requestTimeoutSeconds, maxResponseBytes)),
            Options.Create(new ItsmRecordLookupOptions
            {
                Provider = "TuruncuHat",
                OrBaseObject = "SyntheticRequest",
                OcoBaseObject = ocoBaseObject
            }),
            new EnterpriseIntegrationTelemetry(),
            NullLogger<TuruncuHatItsmRecordLookup>.Instance);

    private static TuruncuHatItsmRecordLookup LookupWithRealSessions(ScriptedHandler handler, ILogger<TuruncuHatItsmRecordLookup>? logger = null)
    {
        HttpClient client = Client(handler);
        TuruncuHatOptions wire = Wire(30, 1_048_576);
        TuruncuHatSessionManager sessions = new(client, Options.Create(wire), TimeProvider.System,
            new EnterpriseIntegrationHealthState(), new EnterpriseIntegrationTelemetry(), NullLogger<TuruncuHatSessionManager>.Instance);
        return new(client, sessions, Options.Create(wire),
            Options.Create(new ItsmRecordLookupOptions { Provider = "TuruncuHat", OrBaseObject = "SyntheticRequest", OcoBaseObject = "SyntheticChange" }),
            new EnterpriseIntegrationTelemetry(), logger ?? NullLogger<TuruncuHatItsmRecordLookup>.Instance);
    }

    private static TuruncuHatOptions Wire(int requestTimeoutSeconds, int maxResponseBytes) => new()
    {
        BaseUrl = _baseUrl,
        Authorization = "Basic c3ludGhldGljOnN5bnRoZXRpYw==",
        Username = "synthetic-user",
        Password = "synthetic-password",
        TenantId = 7,
        SessionLifetimeSeconds = 600,
        RequestTimeoutSeconds = requestTimeoutSeconds,
        MaxResponseBytes = maxResponseBytes
    };

    private static ITuruncuHatSessionManager Sessions()
    {
        ITuruncuHatSessionManager sessions = Substitute.For<ITuruncuHatSessionManager>();
        sessions.GetSessionAsync(Arg.Any<CancellationToken>()).Returns(_session);
        return sessions;
    }

    private static HttpClient Client(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri(_baseUrl), Timeout = Timeout.InfiniteTimeSpan };

    private static HttpResponseMessage Ok(string body) => Response(HttpStatusCode.OK, body);

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    private sealed class ScriptedHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<(string Path, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.AbsolutePath, body));
            return _responses.Dequeue();
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class HangingHandler(Action? onSend = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onSend?.Invoke();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class CapturingLogger : ILogger<TuruncuHatItsmRecordLookup>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception) + " " + exception);
    }
}
