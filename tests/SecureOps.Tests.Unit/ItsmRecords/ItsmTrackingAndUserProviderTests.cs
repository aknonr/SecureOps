using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecureOps.Infrastructure;
using SecureOps.Infrastructure.ItsmRecords;

namespace SecureOps.Tests.Unit.ItsmRecords;

public sealed class ItsmTrackingAndUserProviderTests
{
    [Theory]
    [InlineData("1", "1")]
    [InlineData(" 700456 ", "700456")]
    [InlineData("00012", "00012")]
    [InlineData("12345678901234567890", "12345678901234567890")]
    public void SourceId_ValidInput_IsTrimmed(string input, string expected) =>
        ItsmSourceId.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("1a")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("1 2")]
    [InlineData("1'")]
    [InlineData("１２")]
    [InlineData("123456789012345678901")]
    [InlineData(null)]
    public void SourceId_AnythingElse_IsRejected(string? input) =>
        ItsmSourceId.Normalize(input).Should().BeNull();

    [Theory]
    [InlineData("900001", ItsmRecordLookupStatus.Found, ItsmRecordLookupReason.None)]
    [InlineData(" 900001 ", ItsmRecordLookupStatus.Found, ItsmRecordLookupReason.None)]
    [InlineData("900002", ItsmRecordLookupStatus.Ambiguous, ItsmRecordLookupReason.None)]
    [InlineData("900003", ItsmRecordLookupStatus.Unknown, ItsmRecordLookupReason.Unavailable)]
    [InlineData("900004", ItsmRecordLookupStatus.Found, ItsmRecordLookupReason.None)]
    [InlineData("900404", ItsmRecordLookupStatus.NotFound, ItsmRecordLookupReason.None)]
    [InlineData("9'", ItsmRecordLookupStatus.Unknown, ItsmRecordLookupReason.InvalidIdentifier)]
    public async Task FakeTracking_IsDeterministic_AndCoversEveryState(
        string id, ItsmRecordLookupStatus status, ItsmRecordLookupReason reason)
    {
        FakeItsmRecordTracking fake = new();

        ItsmRecordTrackingResult first = await fake.GetOrTrackingAsync(id, CancellationToken.None);
        ItsmRecordTrackingResult second = await fake.GetOrTrackingAsync(id, CancellationToken.None);

        first.Status.Should().Be(status);
        first.Reason.Should().Be(reason);
        first.Provider.Should().Be("Fake");
        second.Should().BeEquivalentTo(first);
        (first.State is not null).Should().Be(status == ItsmRecordLookupStatus.Found);
    }

    [Fact]
    public async Task FakeTracking_FullRecord_HasKnownPartsAndAnIncompleteVariant()
    {
        FakeItsmRecordTracking fake = new();

        ItsmRecordTrackingResult full = await fake.GetOrTrackingAsync("900001", CancellationToken.None);
        ItsmRecordTrackingResult partial = await fake.GetOrTrackingAsync("900004", CancellationToken.None);

        full.Activities!.State.Should().Be(ItsmTrackingPartState.Known);
        full.Activities.Value.Should().HaveCount(2);
        full.Comments!.Value!.Count.Should().Be(2);
        full.State.Should().Be(new ItsmOrState("2026-01-05T02:30:00", "9301", "9401", "Synthetic status", "Synthetic stage"));
        partial.State.Should().Be(new ItsmOrState(null, null, null));
        partial.Activities!.State.Should().Be(ItsmTrackingPartState.Unknown);
        partial.Activities.Reason.Should().Be(ItsmRecordLookupReason.IncompleteResponse);
        partial.Comments!.State.Should().Be(ItsmTrackingPartState.Known);
    }

    [Fact]
    public void FakeTracking_ExposesNoPersonOrCommentTextMember()
    {
        string[] members = [.. typeof(ItsmActivity).GetProperties().Select(p => p.Name),
            .. typeof(ItsmCommentSummary).GetProperties().Select(p => p.Name),
            .. typeof(ItsmOrState).GetProperties().Select(p => p.Name),
            .. typeof(ItsmUserReference).GetProperties().Select(p => p.Name)];

        members.Should().NotContain(name =>
            name.Contains("Name", StringComparison.OrdinalIgnoreCase) || name.Contains("Mail", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Phone", StringComparison.OrdinalIgnoreCase) || name.Contains("DoneBy", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Author", StringComparison.OrdinalIgnoreCase) || name.Contains("Registration", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Text", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("synthetic.user", ItsmUserMatchStatus.Matched, ItsmRecordLookupReason.None)]
    [InlineData("Synthetic.User", ItsmUserMatchStatus.NotMatched, ItsmRecordLookupReason.None)]
    [InlineData("synthetic.dup", ItsmUserMatchStatus.Ambiguous, ItsmRecordLookupReason.None)]
    [InlineData("synthetic.down", ItsmUserMatchStatus.Unknown, ItsmRecordLookupReason.Unavailable)]
    [InlineData("nobody", ItsmUserMatchStatus.NotMatched, ItsmRecordLookupReason.None)]
    [InlineData("no body", ItsmUserMatchStatus.Unknown, ItsmRecordLookupReason.InvalidIdentifier)]
    [InlineData(null, ItsmUserMatchStatus.Unknown, ItsmRecordLookupReason.InvalidIdentifier)]
    public async Task FakeUserMapping_IsDeterministic_AndCoversEveryState(
        string? name, ItsmUserMatchStatus status, ItsmRecordLookupReason reason)
    {
        FakeItsmUserMapping fake = new();

        ItsmUserMatchResult first = await fake.MatchByUserNameAsync(name, CancellationToken.None);
        ItsmUserMatchResult second = await fake.MatchByUserNameAsync(name, CancellationToken.None);

        first.Status.Should().Be(status);
        first.Reason.Should().Be(reason);
        first.Provider.Should().Be("Fake");
        second.Should().Be(first);
        (first.User is not null).Should().Be(status == ItsmUserMatchStatus.Matched);
    }

    [Theory]
    [InlineData("990001", ItsmUserMatchStatus.Matched, ItsmRecordLookupReason.None)]
    [InlineData("0990001", ItsmUserMatchStatus.Matched, ItsmRecordLookupReason.None)]
    [InlineData("990002", ItsmUserMatchStatus.Ambiguous, ItsmRecordLookupReason.None)]
    [InlineData("990003", ItsmUserMatchStatus.Unknown, ItsmRecordLookupReason.Unavailable)]
    [InlineData("990004", ItsmUserMatchStatus.Matched, ItsmRecordLookupReason.None)]
    [InlineData("990404", ItsmUserMatchStatus.NotMatched, ItsmRecordLookupReason.None)]
    [InlineData("99-001", ItsmUserMatchStatus.Unknown, ItsmRecordLookupReason.InvalidIdentifier)]
    [InlineData("99a001", ItsmUserMatchStatus.NotMatched, ItsmRecordLookupReason.None)]
    [InlineData("0000", ItsmUserMatchStatus.Unknown, ItsmRecordLookupReason.InvalidIdentifier)]
    [InlineData(null, ItsmUserMatchStatus.Unknown, ItsmRecordLookupReason.InvalidIdentifier)]
    public async Task FakeRegistrationMapping_IsDeterministic_AndCoversEveryState(
        string? registrationNumber, ItsmUserMatchStatus status, ItsmRecordLookupReason reason)
    {
        FakeItsmUserMapping fake = new();

        ItsmUserMatchResult first = await fake.MatchByRegistrationNumberAsync(registrationNumber, CancellationToken.None);
        ItsmUserMatchResult second = await fake.MatchByRegistrationNumberAsync(registrationNumber, CancellationToken.None);

        first.Status.Should().Be(status);
        first.Reason.Should().Be(reason);
        first.Provider.Should().Be("Fake");
        second.Should().Be(first);
        (first.User is not null).Should().Be(status == ItsmUserMatchStatus.Matched);
    }

    [Theory]
    [InlineData("990001", "synthetic.user", ItsmUserMatchStatus.Matched, ItsmRecordLookupReason.None)]
    [InlineData("0990001", "Synthetic.User", ItsmUserMatchStatus.Matched, ItsmRecordLookupReason.None)]
    [InlineData("990004", "synthetic.user", ItsmUserMatchStatus.Ambiguous, ItsmRecordLookupReason.None)]
    [InlineData("990003", "synthetic.user", ItsmUserMatchStatus.Unknown, ItsmRecordLookupReason.Unavailable)]
    [InlineData("990002", "synthetic.user", ItsmUserMatchStatus.Ambiguous, ItsmRecordLookupReason.None)]
    [InlineData("990404", "synthetic.user", ItsmUserMatchStatus.Matched, ItsmRecordLookupReason.None)]
    [InlineData("990404", "nobody", ItsmUserMatchStatus.NotMatched, ItsmRecordLookupReason.None)]
    [InlineData("990001", "nobody", ItsmUserMatchStatus.Matched, ItsmRecordLookupReason.None)]
    [InlineData("990001", "synthetic.down", ItsmUserMatchStatus.Unknown, ItsmRecordLookupReason.Unavailable)]
    [InlineData("990001", "synthetic.dup", ItsmUserMatchStatus.Ambiguous, ItsmRecordLookupReason.None)]
    [InlineData("99-001", "synthetic.user", ItsmUserMatchStatus.Unknown, ItsmRecordLookupReason.InvalidIdentifier)]
    [InlineData(null, "synthetic.user", ItsmUserMatchStatus.Matched, ItsmRecordLookupReason.None)]
    [InlineData("990001", null, ItsmUserMatchStatus.Matched, ItsmRecordLookupReason.None)]
    [InlineData(null, null, ItsmUserMatchStatus.Unknown, ItsmRecordLookupReason.InvalidIdentifier)]
    [InlineData(" ", "", ItsmUserMatchStatus.Unknown, ItsmRecordLookupReason.InvalidIdentifier)]
    public async Task FakeCombinedMapping_FollowsThePriorityAndConflictRule(
        string? registrationNumber, string? userName, ItsmUserMatchStatus status, ItsmRecordLookupReason reason)
    {
        ItsmUserMatchResult result = await new FakeItsmUserMapping().MatchAsync(registrationNumber, userName, CancellationToken.None);

        result.Status.Should().Be(status);
        result.Reason.Should().Be(reason);
        result.Provider.Should().Be("Fake");
        (result.User is not null).Should().Be(status == ItsmUserMatchStatus.Matched);
    }

    [Fact]
    public async Task FakeCombinedMapping_ConflictNeverReturnsEitherUser()
    {
        FakeItsmUserMapping fake = new();

        ItsmUserMatchResult byRegistration = await fake.MatchByRegistrationNumberAsync("990004", CancellationToken.None);
        ItsmUserMatchResult byUserName = await fake.MatchByUserNameAsync("synthetic.user", CancellationToken.None);
        ItsmUserMatchResult combined = await fake.MatchAsync("990004", "synthetic.user", CancellationToken.None);

        byRegistration.User!.UserId.Should().NotBe(byUserName.User!.UserId);
        combined.Status.Should().Be(ItsmUserMatchStatus.Ambiguous);
        combined.User.Should().BeNull();
    }

    [Fact]
    public async Task Disabled_TrackingAndUserMapping_AreAlwaysUnknownNotConfigured()
    {
        DisabledItsmUserMapping disabled = new();
        ItsmRecordTrackingResult tracking = await new DisabledItsmRecordTracking().GetOrTrackingAsync("900001", CancellationToken.None);
        ItsmUserMatchResult[] users =
        [
            await disabled.MatchByUserNameAsync("synthetic.user", CancellationToken.None),
            await disabled.MatchByRegistrationNumberAsync("990001", CancellationToken.None),
            await disabled.MatchAsync("990001", "synthetic.user", CancellationToken.None)
        ];

        tracking.Status.Should().Be(ItsmRecordLookupStatus.Unknown);
        tracking.Reason.Should().Be(ItsmRecordLookupReason.NotConfigured);
        tracking.Provider.Should().Be("Disabled");
        users.Should().AllSatisfy(user =>
        {
            user.Status.Should().Be(ItsmUserMatchStatus.Unknown);
            user.Reason.Should().Be(ItsmRecordLookupReason.NotConfigured);
            user.Provider.Should().Be("Disabled");
        });
    }

    [Fact]
    public void Registration_Default_RegistersDisabledForAllThree()
    {
        using ServiceProvider provider = Build(new Dictionary<string, string?>(), "Production");

        provider.GetRequiredService<IItsmRecordTracking>().Should().BeOfType<DisabledItsmRecordTracking>();
        provider.GetRequiredService<IItsmUserMapping>().Should().BeOfType<DisabledItsmUserMapping>();
    }

    [Fact]
    public void Registration_Fake_InSyntheticEnvironment_RegistersFakeForAllThree()
    {
        using ServiceProvider provider = Build(new() { ["ItsmRecordLookup:Provider"] = "Fake" }, "Test");

        provider.GetRequiredService<IItsmRecordTracking>().Should().BeOfType<FakeItsmRecordTracking>();
        provider.GetRequiredService<IItsmUserMapping>().Should().BeOfType<FakeItsmUserMapping>();
    }

    [Fact]
    public void Registration_TuruncuHat_RegistersTheRealAdapters_NotFake()
    {
        using ServiceProvider provider = Build(TuruncuHat(), "Production");

        provider.GetRequiredService<IItsmRecordTracking>().Should().BeOfType<TuruncuHatItsmRecordTracking>();
        provider.GetRequiredService<IItsmUserMapping>().Should().BeOfType<TuruncuHatItsmUserMapping>();
    }

    [Fact]
    public void Validation_Fake_OutsideSyntheticEnvironment_StillFailsAtStartup()
    {
        Action act = () => Build(new() { ["ItsmRecordLookup:Provider"] = "Fake" }, "Production");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Fake is permitted only*");
    }

    [Theory]
    [InlineData("ItsmRecordLookup:OrStatusField", "bad name")]
    [InlineData("ItsmRecordLookup:OrStageField", "a'b")]
    [InlineData("ItsmRecordLookup:CommentMainObject", "1 OR 1")]
    [InlineData("ItsmRecordLookup:ActivityMainObject", "#%x%#")]
    [InlineData("ItsmRecordLookup:CommentTimeField", "")]
    [InlineData("ItsmRecordLookup:CommentTimeField", "m_created_dt'")]
    public void Validation_TuruncuHat_UnsafeTrackingOption_FailsAtStartup(string key, string value)
    {
        Dictionary<string, string?> settings = TuruncuHat();
        settings[key] = value;

        Action act = () => ItsmRecordLookupRegistration.Validate(Configuration(settings), "Production");

        act.Should().Throw<InvalidOperationException>().WithMessage("*identifier*");
    }

    [Fact]
    public void Validation_TuruncuHat_EmptyOptionalTrackingOptions_AreAccepted()
    {
        Dictionary<string, string?> settings = TuruncuHat();
        settings["ItsmRecordLookup:OrStatusField"] = "";
        settings["ItsmRecordLookup:CommentMainObject"] = "";

        ItsmRecordLookupRegistration.Validate(Configuration(settings), "Production").Should().Be("TuruncuHat");
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings, string environment)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSecureOpsInfrastructure(Configuration(settings), environment);
        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static Dictionary<string, string?> TuruncuHat() => new()
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
}
