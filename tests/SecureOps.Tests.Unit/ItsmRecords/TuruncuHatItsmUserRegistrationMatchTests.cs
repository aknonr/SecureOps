using System.Diagnostics.Metrics;
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

/// <summary>Registration-number path and combined match of the Turuncu Hat user mapping (ADR-0032 amendment 2026-10-13).</summary>
public sealed class TuruncuHatItsmUserRegistrationMatchTests
{
    private const string _session = "synthetic-session";

    [Fact]
    public async Task SingleExactRow_WithLeadingZeroDropped_IsMatched_WithIdsOnly()
    {
        ScriptedHandler handler = new(Ok(Query(RegistrationRow)));

        ItsmUserMatchResult result = await Mapping(handler).MatchByRegistrationNumberAsync(" " + RegistrationNumber + " ", CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Matched);
        result.Reason.Should().Be(ItsmRecordLookupReason.None);
        result.Provider.Should().Be("TuruncuHat");
        result.User.Should().Be(new ItsmUserReference("880001", "8601", "8701"));
        result.ToString().Should().NotContain(SearchedRegistrationNumber);
    }

    [Fact]
    public async Task Request_IsOneExactEquality_WithoutLeadingZeros_SelectOnly_WithoutPersonFields()
    {
        ScriptedHandler handler = new(Ok(Query(RegistrationRow)));

        await Mapping(handler).MatchByRegistrationNumberAsync(RegistrationNumber, CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Path.Should().Be("/api/query");
        JsonElement req = JsonDocument.Parse(handler.Requests[0].Body).RootElement.GetProperty("req");
        req.GetProperty("BaseObject").GetString().Should().Be("cmn_user");
        req.GetProperty("Filters").EnumerateArray().Select(f => f.GetString()).Should()
            .Equal("#%c_registration_number%#='770123'");
        req.GetProperty("Selects").EnumerateArray().Select(s => s.GetString()).Should()
            .Equal("id", "c_registration_number", "p_obs_id", "m_rel_home_lbs_id");
        req.GetProperty("NoEncode").GetBoolean().Should().BeTrue();
        req.TryGetProperty("QueryPermissions", out _).Should().BeFalse();
        handler.Requests[0].Body.Should().NotContainAny(RegistrationNumber, "m_username", "m_phone_number", "m_email", "m_name", "Password");
    }

    [Theory]
    [InlineData("770123")]
    [InlineData("000770123")]
    [InlineData(" 770123 ")]
    public async Task ReturnedNumber_IsNormalizedBeforeTheCheck(string returned)
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Ok(Query(RegistrationRow.With("SET.c_registration_number", returned)))))
            .MatchByRegistrationNumberAsync(RegistrationNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Matched);
    }

    [Fact]
    public async Task ReturnedNumberAsJsonNumber_IsMatched()
    {
        const string body = """
            {"QueryResult":{"ErrorNo":0,"Items":[[{"Key":"SET.id","Value":880001},{"Key":"SET.c_registration_number","Value":770123},{"Key":"num","Value":"1"}]],"MaxPages":1,"PageNo":1,"RecordCount":1}}
            """;

        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Ok(body)))
            .MatchByRegistrationNumberAsync(RegistrationNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Matched);
        result.User.Should().Be(new ItsmUserReference("880001", null, null));
    }

    [Theory]
    [InlineData("770124")]
    [InlineData("1770123")]
    [InlineData("7701230")]
    [InlineData("77O123")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData(null)]
    public async Task RowForAnotherOrUnreadableNumber_IsUnknownInvalidResponse(string? returned)
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Ok(Query(RegistrationRow.With("SET.c_registration_number", returned)))))
            .MatchByRegistrationNumberAsync(RegistrationNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.InvalidResponse);
    }

    [Fact]
    public async Task CompleteResponseWithZeroRows_IsNotMatched()
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Ok(VendorEnvelope(0, 0, 0))))
            .MatchByRegistrationNumberAsync(RegistrationNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.NotMatched);
        result.User.Should().BeNull();
    }

    [Fact]
    public async Task TwoRows_AreAmbiguous_AndNoUserIsChosen()
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Ok(Query(RegistrationRow, RegistrationRow.With("SET.id", "880002")))))
            .MatchByRegistrationNumberAsync(RegistrationNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Ambiguous);
        result.User.Should().BeNull();
    }

    [Theory]
    [InlineData("SET.id")]
    [InlineData("SET.c_registration_number")]
    public async Task MissingRequiredKey_IsUnknownIncomplete(string missing)
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Ok(Query(RegistrationRow.Without(missing)))))
            .MatchByRegistrationNumberAsync(RegistrationNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.IncompleteResponse);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 5)]
    public async Task MorePagesOrRecordsThanReturned_IsUnknown_NeverNotMatched(int maxPages, int recordCount)
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Ok(Envelope([], maxPages, recordCount))))
            .MatchByRegistrationNumberAsync(RegistrationNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.IncompleteResponse);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, ItsmRecordLookupReason.Unavailable)]
    [InlineData(HttpStatusCode.BadRequest, ItsmRecordLookupReason.Rejected)]
    public async Task HttpFailure_IsUnknown_NeverNotMatched(HttpStatusCode status, ItsmRecordLookupReason reason)
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Response(status, "{}")))
            .MatchByRegistrationNumberAsync(RegistrationNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Unknown);
        result.Reason.Should().Be(reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("000000")]
    [InlineData("12a456")]
    [InlineData("A12345")]
    [InlineData("123 456")]
    [InlineData("123\t456")]
    [InlineData("123\n456")]
    [InlineData("-123456")]
    [InlineData("+123456")]
    [InlineData("123.456")]
    [InlineData("1e5")]
    [InlineData("123456789012345678901")]
    [InlineData("000000000000000000001")]
    [InlineData("١٢٣٤٥٦")]
    [InlineData("１２３４５６")]
    [InlineData("770123' OR '1'='1")]
    [InlineData("770123'")]
    [InlineData("%")]
    [InlineData("770%")]
    [InlineData("770*")]
    [InlineData("#%c_registration_number%#")]
    public async Task InvalidRegistrationNumber_IsUnknownInvalidIdentifier_WithoutAnyRequest(string? registrationNumber)
    {
        ScriptedHandler handler = new();
        ITuruncuHatSessionManager sessions = Sessions();

        ItsmUserMatchResult result = await Mapping(handler, sessions: sessions)
            .MatchByRegistrationNumberAsync(registrationNumber, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.InvalidIdentifier);
        handler.Requests.Should().BeEmpty();
        await sessions.DidNotReceive().GetSessionAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("000123", "123")]
    [InlineData(" 0770123 ", "770123")]
    [InlineData("100200", "100200")]
    [InlineData("12345678901234567890", "12345678901234567890")]
    [InlineData("00000000000000000001", "1")]
    public void RegistrationNumber_ValidInput_DropsLeadingZeros(string input, string expected) =>
        ItsmRegistrationNumber.Normalize(input).Should().Be(expected);

    [Fact]
    public async Task CallerCancellation_Propagates_BeforeAnyRequest()
    {
        ScriptedHandler handler = new();
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        Func<Task> byNumber = () => Mapping(handler).MatchByRegistrationNumberAsync(RegistrationNumber, cancelled.Token);
        Func<Task> combined = () => Mapping(handler).MatchAsync(RegistrationNumber, UserName, cancelled.Token);

        await byNumber.Should().ThrowAsync<OperationCanceledException>();
        await combined.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().BeEmpty();
    }

    // Combined match: registration number first, user name second.

    [Fact]
    public async Task Combined_BothKeysOnTheSameUser_IsMatched_RegistrationQueriedFirst()
    {
        ScriptedHandler handler = new(Ok(Query(RegistrationRow)), Ok(Query(UserRow)));

        ItsmUserMatchResult result = await Mapping(handler).MatchAsync(RegistrationNumber, UserName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Matched);
        result.User.Should().Be(new ItsmUserReference("880001", "8601", "8701"));
        Filters(handler).Should().Equal("#%c_registration_number%#='770123'", "#%m_username%#='synthetic.user'");
    }

    [Fact]
    public async Task Combined_KeysOnDifferentUsers_IsAmbiguousConflict_WithoutAGuess()
    {
        CapturingLogger<TuruncuHatItsmUserMapping> logger = new();
        ScriptedHandler handler = new(Ok(Query(RegistrationRow)), Ok(Query(UserRow.With("SET.id", "880009"))));

        ItsmUserMatchResult result = await Mapping(handler, logger: logger).MatchAsync(RegistrationNumber, UserName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Ambiguous);
        result.Reason.Should().Be(ItsmRecordLookupReason.None);
        result.User.Should().BeNull();
        logger.Messages.Should().ContainSingle(message => message.Contains("different users", StringComparison.Ordinal));
        string.Join('\n', logger.Messages).Should().NotContainAny(SearchedRegistrationNumber, UserName, "880001", "880009");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, ItsmRecordLookupReason.Unavailable)]
    [InlineData(HttpStatusCode.BadRequest, ItsmRecordLookupReason.Rejected)]
    public async Task Combined_UnknownRegistration_NeverFallsBackToTheUserName(HttpStatusCode status, ItsmRecordLookupReason reason)
    {
        ScriptedHandler handler = new(Response(status, "{}"), Ok(Query(UserRow)));

        ItsmUserMatchResult result = await Mapping(handler).MatchAsync(RegistrationNumber, UserName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Unknown);
        result.Reason.Should().Be(reason);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Combined_IncompleteRegistrationAnswer_NeverFallsBackToTheUserName()
    {
        ScriptedHandler handler = new(Ok(Envelope([], 2, 1)), Ok(Query(UserRow)));

        ItsmUserMatchResult result = await Mapping(handler).MatchAsync(RegistrationNumber, UserName, CancellationToken.None);

        result.Reason.Should().Be(ItsmRecordLookupReason.IncompleteResponse);
        handler.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("12a456")]
    [InlineData("000000")]
    [InlineData("770123' OR '1'='1")]
    public async Task Combined_InvalidRegistration_IsUnknownInvalidIdentifier_WithoutFallbackOrRequest(string registrationNumber)
    {
        ScriptedHandler handler = new();

        ItsmUserMatchResult result = await Mapping(handler).MatchAsync(registrationNumber, UserName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.InvalidIdentifier);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Combined_AmbiguousRegistration_StopsWithoutTheUserName()
    {
        ScriptedHandler handler = new(Ok(Query(RegistrationRow, RegistrationRow.With("SET.id", "880002"))));

        ItsmUserMatchResult result = await Mapping(handler).MatchAsync(RegistrationNumber, UserName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Ambiguous);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Combined_NotMatchedRegistration_TriesTheUserName()
    {
        ScriptedHandler handler = new(Ok(Query()), Ok(Query(UserRow)));

        ItsmUserMatchResult result = await Mapping(handler).MatchAsync(RegistrationNumber, UserName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Matched);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Combined_NotMatchedRegistrationAndUserName_IsNotMatched()
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Ok(Query()), Ok(Query())))
            .MatchAsync(RegistrationNumber, UserName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.NotMatched);
    }

    [Fact]
    public async Task Combined_MatchedRegistration_WithUnusedUserName_KeepsTheRegistrationMatch()
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Ok(Query(RegistrationRow)), Ok(Query())))
            .MatchAsync(RegistrationNumber, UserName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Matched);
        result.User!.UserId.Should().Be("880001");
    }

    [Fact]
    public async Task Combined_MatchedRegistration_WithUnprovenUserName_IsUnknown_BecauseNoConflictIsProven()
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(Ok(Query(RegistrationRow)), Response(HttpStatusCode.BadGateway, "{}")))
            .MatchAsync(RegistrationNumber, UserName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.Unavailable);
    }

    [Fact]
    public async Task Combined_MatchedRegistration_WithAmbiguousUserName_IsAmbiguous()
    {
        ItsmUserMatchResult result = await Mapping(new ScriptedHandler(
                Ok(Query(RegistrationRow)), Ok(Query(UserRow, UserRow.With("SET.id", "880002")))))
            .MatchAsync(RegistrationNumber, UserName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Ambiguous);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Combined_OnlyUserName_UsesTheUserNameAlone(string? registrationNumber)
    {
        ScriptedHandler handler = new(Ok(Query(UserRow)));

        ItsmUserMatchResult result = await Mapping(handler).MatchAsync(registrationNumber, UserName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Matched);
        Filters(handler).Should().Equal("#%m_username%#='synthetic.user'");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Combined_OnlyRegistration_UsesTheRegistrationAlone(string? userName)
    {
        ScriptedHandler handler = new(Ok(Query(RegistrationRow)));

        ItsmUserMatchResult result = await Mapping(handler).MatchAsync(RegistrationNumber, userName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Matched);
        Filters(handler).Should().Equal("#%c_registration_number%#='770123'");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", " ")]
    public async Task Combined_NoKey_IsUnknownInvalidIdentifier_WithoutAnyRequest(string? registrationNumber, string? userName)
    {
        ScriptedHandler handler = new();
        ITuruncuHatSessionManager sessions = Sessions();

        ItsmUserMatchResult result = await Mapping(handler, sessions: sessions).MatchAsync(registrationNumber, userName, CancellationToken.None);

        result.Status.Should().Be(ItsmUserMatchStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.InvalidIdentifier);
        handler.Requests.Should().BeEmpty();
        await sessions.DidNotReceive().GetSessionAsync(Arg.Any<CancellationToken>());
    }

    // Personal-data boundary: the registration number is a query key only.

    [Fact]
    public async Task Logs_NeverContainRegistrationNumberSessionCredentialsOrUrl()
    {
        CapturingLogger<TuruncuHatItsmUserMapping> logger = new();
        ScriptedHandler handler = new(
            Response(HttpStatusCode.InternalServerError, "{}"),
            Ok(ApplicationError),
            Ok("not json"),
            Ok(Query(RegistrationRow.Without("SET.id"))),
            Ok(Query(RegistrationRow.With("SET.c_registration_number", "770124"))),
            Ok(Query(RegistrationRow)), Ok(Query(UserRow.With("SET.id", "880009"))));
        TuruncuHatItsmUserMapping mapping = Mapping(handler, logger: logger);

        for (int i = 0; i < 5; i++)
        {
            await mapping.MatchByRegistrationNumberAsync(RegistrationNumber, CancellationToken.None);
        }

        await mapping.MatchAsync(RegistrationNumber, UserName, CancellationToken.None);

        logger.Messages.Should().HaveCountGreaterThan(5);
        string all = string.Join('\n', logger.Messages);
        all.Should().Contain("registration number");
        all.Should().NotContainAny(SearchedRegistrationNumber, "770124", _session, "synthetic-user", "synthetic-password",
            "Basic c3ludGhldGlj", "itsm.invalid", UserName, "880001");
    }

    [Fact]
    public async Task Telemetry_CarriesOnlyProviderOperationAndOutcome_NeverTheRegistrationNumber()
    {
        List<KeyValuePair<string, object?>> tags = [];
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "SecureOps.EnterpriseIntegrations")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, measurementTags, _) =>
        {
            lock (tags)
            {
                tags.AddRange(measurementTags.ToArray());
            }
        });
        listener.Start();
        ScriptedHandler handler = new(
            Ok(Query(RegistrationRow)),
            Ok(Query(RegistrationRow)), Ok(Query(UserRow.With("SET.id", "880009"))),
            Response(HttpStatusCode.InternalServerError, "{}"));
        TuruncuHatItsmUserMapping mapping = Mapping(handler);

        await mapping.MatchByRegistrationNumberAsync(RegistrationNumber, CancellationToken.None);
        await mapping.MatchAsync(RegistrationNumber, UserName, CancellationToken.None);
        await mapping.MatchByRegistrationNumberAsync(RegistrationNumber, CancellationToken.None);
        listener.Dispose();

        KeyValuePair<string, object?>[] captured;
        lock (tags)
        {
            captured = [.. tags];
        }

        captured.Any(tag => tag.Key == "operation" && Equals(tag.Value, "itsm-user-match-registration")).Should().BeTrue();
        captured.Any(tag => tag.Key == "operation" && Equals(tag.Value, "itsm-user-match-combined")).Should().BeTrue();
        captured.Any(tag => tag.Key == "outcome" && Equals(tag.Value, "conflict")).Should().BeTrue();
        captured.Select(tag => tag.Key).Distinct().Should().BeSubsetOf(["provider", "operation", "outcome"]);
        captured.Select(tag => tag.Value?.ToString() ?? string.Empty).Should()
            .NotContain(value => value.Contains(SearchedRegistrationNumber, StringComparison.Ordinal)
                || value.Contains(UserName, StringComparison.Ordinal));
    }

    // Rule order, checked against the interface alone so every provider behaves the same.

    [Theory]
    [InlineData(ItsmUserMatchStatus.Unknown)]
    [InlineData(ItsmUserMatchStatus.Ambiguous)]
    public async Task Rules_UnknownOrAmbiguousRegistration_NeverCallsTheUserNamePath(ItsmUserMatchStatus status)
    {
        IItsmUserMapping mapping = Substitute.For<IItsmUserMapping>();
        mapping.MatchByRegistrationNumberAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ItsmUserMatchResult(status, status == ItsmUserMatchStatus.Unknown
                ? ItsmRecordLookupReason.Timeout : ItsmRecordLookupReason.None, null, "Synthetic"));

        (ItsmUserMatchResult result, bool conflict) = await ItsmUserMatchRules.MatchAsync(
            mapping, RegistrationNumber, UserName, "Synthetic", CancellationToken.None);

        result.Status.Should().Be(status);
        conflict.Should().BeFalse();
        await mapping.DidNotReceive().MatchByUserNameAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    private static IEnumerable<string?> Filters(ScriptedHandler handler) =>
        handler.Requests.Select(request => JsonDocument.Parse(request.Body).RootElement
            .GetProperty("req").GetProperty("Filters")[0].GetString());

    private static TuruncuHatItsmUserMapping Mapping(
        HttpMessageHandler handler,
        ITuruncuHatSessionManager? sessions = null,
        ILogger<TuruncuHatItsmUserMapping>? logger = null) =>
        new(Client(handler), sessions ?? Sessions(),
            Options.Create(new TuruncuHatOptions
            {
                BaseUrl = BaseUrl,
                Authorization = "Basic c3ludGhldGljOnN5bnRoZXRpYw==",
                Username = "synthetic-user",
                Password = "synthetic-password",
                TenantId = 7,
                SessionLifetimeSeconds = 600,
                RequestTimeoutSeconds = 30,
                MaxResponseBytes = 1_048_576
            }),
            new EnterpriseIntegrationTelemetry(), logger ?? NullLogger<TuruncuHatItsmUserMapping>.Instance);

    private static ITuruncuHatSessionManager Sessions()
    {
        ITuruncuHatSessionManager sessions = Substitute.For<ITuruncuHatSessionManager>();
        sessions.GetSessionAsync(Arg.Any<CancellationToken>()).Returns(_session);
        return sessions;
    }
}
