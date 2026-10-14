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

public sealed class TuruncuHatItsmUserMappingTests
{
    private const string _session = "synthetic-session";

    [Fact]
    public async Task SingleExactRow_IsMatched_WithIdsOnly()
    {
        ScriptedHandler handler = new(Ok(Query(UserRow)));

        ItsmUserMatchResult result = await Mapping(handler).MatchByUserNameAsync(" " + UserName + " ", CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Matched);
        result.Reason.Should().Be(ItsmRecordLookupReason.None);
        result.Provider.Should().Be("TuruncuHat");
        result.User.Should().Be(new ItsmUserReference("880001", "8601", "8701"));
    }

    [Fact]
    public async Task Request_IsOneExactEqualityOnActiveUsers_SelectOnly_WithoutPersonFields()
    {
        ScriptedHandler handler = new(Ok(Query(UserRow)));

        await Mapping(handler).MatchByUserNameAsync(UserName, CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Path.Should().Be("/api/query");
        JsonElement req = JsonDocument.Parse(handler.Requests[0].Body).RootElement.GetProperty("req");
        req.GetProperty("BaseObject").GetString().Should().Be("cmn_user");
        req.GetProperty("Filters").EnumerateArray().Select(f => f.GetString()).Should().Equal("#%m_active%#='True' AND #%m_username%#='synthetic.user'");
        req.GetProperty("Selects").EnumerateArray().Select(s => s.GetString()).Should()
            .Equal("id", "m_username", "p_obs_id", "m_rel_home_lbs_id");
        req.GetProperty("NoEncode").GetBoolean().Should().BeTrue();
        req.TryGetProperty("QueryPermissions", out _).Should().BeFalse();
        handler.Requests[0].Body.Should().NotContainAny("m_phone_number", "c_registration_number", "m_email", "m_name", "Password");
    }

    [Fact]
    public async Task EmptyOptionalIds_AreNull()
    {
        ScriptedHandler handler = new(Ok(Query(UserRow.With("SET.p_obs_id", null).Without("SET.m_rel_home_lbs_id"))));

        ItsmUserMatchResult result = await Mapping(handler).MatchByUserNameAsync(UserName, CancellationToken.None);

        result.User.Should().Be(new ItsmUserReference("880001", null, null));
    }

    // The source compares m_username case-sensitively (Turuncu Hat team, 2026-10-14): a row in another case did not
    // honour the equality filter as this adapter understands it, so nothing in the response is trusted.
    [Theory]
    [InlineData("Synthetic.User")]
    [InlineData("SYNTHETIC.USER")]
    public async Task ReturnedNameCaseDifference_IsUnknownInvalidResponse(string returned)
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Ok(Query(UserRow.With("SET.m_username", returned)))))
            .MatchByUserNameAsync(UserName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.InvalidResponse);
    }

    [Fact]
    public async Task UserName_IsSentInItsOwnCase_AndMatchedOrdinally()
    {
        ScriptedHandler handler = new(Ok(Query(UserRow.With("SET.m_username", "Synthetic.User"))));

        ItsmUserMatchResult result = await Mapping(handler).MatchByUserNameAsync("Synthetic.User", CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Matched);
        JsonDocument.Parse(handler.Requests[0].Body).RootElement.GetProperty("req").GetProperty("Filters")[0].GetString()
            .Should().Be("#%m_active%#='True' AND #%m_username%#='Synthetic.User'");
    }

    [Fact]
    public async Task CompleteResponseWithZeroRows_IsNotMatched()
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Ok(Query()))).MatchByUserNameAsync(UserName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.NotMatched);
        result.User.Should().BeNull();
    }

    [Fact]
    public async Task TwoRows_AreAmbiguous_AndNoUserIsChosen()
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Ok(Query(UserRow, UserRow.With("SET.id", "880002")))))
            .MatchByUserNameAsync(UserName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Ambiguous);
        result.User.Should().BeNull();
    }

    [Fact]
    public async Task RowForAnotherUserName_IsUnknownInvalidResponse()
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Ok(Query(UserRow.With("SET.m_username", "someone.else")))))
            .MatchByUserNameAsync(UserName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.InvalidResponse);
    }

    [Theory]
    [InlineData("SET.id")]
    [InlineData("SET.m_username")]
    public async Task MissingRequiredKey_IsUnknownIncomplete(string missing)
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Ok(Query(UserRow.Without(missing)))))
            .MatchByUserNameAsync(UserName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.IncompleteResponse);
    }

    [Fact]
    public async Task EmptyUserId_IsUnknownIncomplete()
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Ok(Query(UserRow.With("SET.id", " ")))))
            .MatchByUserNameAsync(UserName, CancellationToken.None);

        result.Reason.Should().Be(ItsmRecordLookupReason.IncompleteResponse);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 5)]
    public async Task MorePagesOrRecordsThanReturned_IsUnknown_NeverNotMatched(int maxPages, int recordCount)
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Ok(Envelope([], maxPages, recordCount))))
            .MatchByUserNameAsync(UserName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.IncompleteResponse);
    }

    [Fact]
    public async Task VendorSinglePageEnvelope_IsComplete()
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Ok(VendorEnvelope(0, 0, 1, UserRow))))
            .MatchByUserNameAsync(UserName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Matched);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, ItsmRecordLookupReason.Unavailable)]
    [InlineData(HttpStatusCode.BadRequest, ItsmRecordLookupReason.Rejected)]
    public async Task HttpFailure_IsUnknown_NeverNotMatched(HttpStatusCode status, ItsmRecordLookupReason reason)
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Response(status, "{}")))
            .MatchByUserNameAsync(UserName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Unknown);
        result.Reason.Should().Be(reason);
    }

    [Fact]
    public async Task ApplicationErrorEnvelope_IsUnknownRejected()
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Ok(ApplicationError)))
            .MatchByUserNameAsync(UserName, CancellationToken.None);

        result.Reason.Should().Be(ItsmRecordLookupReason.Rejected);
    }

    [Fact]
    public async Task TransportFailureAndTimeout_AreUnknown()
    {
        ItsmUserMatchResult transport = await Mapping(new ThrowingHandler(new HttpRequestException("synthetic")))
            .MatchByUserNameAsync(UserName, CancellationToken.None);
        ItsmUserMatchResult timeout = await Mapping(new HangingHandler(), requestTimeoutSeconds: 1)
            .MatchByUserNameAsync(UserName, CancellationToken.None);

        transport.Reason.Should().Be(ItsmRecordLookupReason.Unavailable);
        timeout.Reason.Should().Be(ItsmRecordLookupReason.Timeout);
    }

    [Fact]
    public async Task SessionRejectedAgainAfterRelogin_IsUnknownAuthenticationFailed()
    {
        ScriptedHandler handler = new(
            Ok("""{"LoginResult":"ok|session-one"}"""), Response(HttpStatusCode.Forbidden, "{}"),
            Ok("""{"LoginResult":"ok|session-two"}"""), Response(HttpStatusCode.Unauthorized, "{}"));
        HttpClient client = Client(handler);
        TuruncuHatOptions wire = Wire(30);
        TuruncuHatSessionManager sessions = new(client, Options.Create(wire), TimeProvider.System,
            new EnterpriseIntegrationHealthState(), new EnterpriseIntegrationTelemetry(), NullLogger<TuruncuHatSessionManager>.Instance);
        TuruncuHatItsmUserMapping mapping = new(client, sessions, Options.Create(wire),
            new EnterpriseIntegrationTelemetry(), NullLogger<TuruncuHatItsmUserMapping>.Instance);

        ItsmUserMatchResult result = await mapping.MatchByUserNameAsync(UserName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.AuthenticationFailed);
        handler.Requests.Should().HaveCount(4);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("synthetic.user' OR '1'='1")]
    [InlineData("synthetic user")]
    [InlineData("domain\\synthetic.user")]
    [InlineData("synthetic.user@example.invalid")]
    [InlineData("synthetic*")]
    [InlineData("%")]
    [InlineData("#%m_username%#")]
    [InlineData(".leading.dot")]
    [InlineData("synthetic.üser")]
    [InlineData("synthetic\n.user")]
    public async Task InvalidUserName_IsUnknownInvalidIdentifier_WithoutAnyRequest(string? userName)
    {
        ScriptedHandler handler = new();
        ITuruncuHatSessionManager sessions = Sessions();

        ItsmUserMatchResult result = await Mapping(handler, sessions: sessions).MatchByUserNameAsync(userName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.InvalidIdentifier);
        handler.Requests.Should().BeEmpty();
        await sessions.DidNotReceive().GetSessionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CallerCancellation_Propagates_BeforeAnyRequest()
    {
        ScriptedHandler handler = new();
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        Func<Task> act = () => Mapping(handler).MatchByUserNameAsync(UserName, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Logs_NeverContainUserNameSessionCredentialsOrUrl()
    {
        CapturingLogger<TuruncuHatItsmUserMapping> logger = new();
        ScriptedHandler handler = new(
            Response(HttpStatusCode.InternalServerError, "{}"),
            Ok(ApplicationError),
            Ok("not json"),
            Ok(Query(UserRow.Without("SET.id"))),
            Ok(Query(UserRow.With("SET.m_username", "someone.else"))),
            Ok(Query(UserRow.With("SET.m_username", "Synthetic.User"))));
        TuruncuHatItsmUserMapping mapping = Mapping(handler, logger: logger);

        for (int i = 0; i < 6; i++)
        {
            await mapping.MatchByUserNameAsync(UserName, CancellationToken.None);
        }

        logger.Messages.Should().NotBeEmpty();
        string all = string.Join('\n', logger.Messages);
        all.Should().NotContainAny(_session, "synthetic-user", "synthetic-password", "Basic c3ludGhldGlj", "itsm.invalid",
            UserName, "Synthetic.User", "someone.else", "880001");
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("  Synthetic.User-1_x ", "Synthetic.User-1_x")]
    public void UserName_ValidInput_IsTrimmed(string input, string expected) =>
        ItsmUserName.Normalize(input).Should().Be(expected);

    [Fact]
    public void UserName_OverLength_IsRejected()
    {
        ItsmUserName.Normalize(new string('a', ItsmUserName.MaxLength)).Should().NotBeNull();
        ItsmUserName.Normalize(new string('a', ItsmUserName.MaxLength + 1)).Should().BeNull();
    }

    private static TuruncuHatItsmUserMapping Mapping(
        HttpMessageHandler handler,
        ITuruncuHatSessionManager? sessions = null,
        int requestTimeoutSeconds = 30,
        ILogger<TuruncuHatItsmUserMapping>? logger = null) =>
        new(Client(handler), sessions ?? Sessions(), Options.Create(Wire(requestTimeoutSeconds)),
            new EnterpriseIntegrationTelemetry(), logger ?? NullLogger<TuruncuHatItsmUserMapping>.Instance);

    private static TuruncuHatOptions Wire(int requestTimeoutSeconds) => new()
    {
        BaseUrl = BaseUrl,
        Authorization = "Basic c3ludGhldGljOnN5bnRoZXRpYw==",
        Username = "synthetic-user",
        Password = "synthetic-password",
        TenantId = 7,
        SessionLifetimeSeconds = 600,
        RequestTimeoutSeconds = requestTimeoutSeconds,
        MaxResponseBytes = 1_048_576
    };

    private static ITuruncuHatSessionManager Sessions()
    {
        ITuruncuHatSessionManager sessions = Substitute.For<ITuruncuHatSessionManager>();
        sessions.GetSessionAsync(Arg.Any<CancellationToken>()).Returns(_session);
        return sessions;
    }
}
