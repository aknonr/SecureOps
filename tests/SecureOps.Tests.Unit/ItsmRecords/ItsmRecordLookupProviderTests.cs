using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecureOps.Infrastructure;
using SecureOps.Infrastructure.ItsmRecords;

namespace SecureOps.Tests.Unit.ItsmRecords;

public sealed class ItsmRecordLookupProviderTests
{
    [Theory]
    [InlineData(ItsmRecordKind.Or, "OR-1", "OR-1")]
    [InlineData(ItsmRecordKind.Or, " or-000456 ", "OR-000456")]
    [InlineData(ItsmRecordKind.Oco, "Oco-000123", "OCO-000123")]
    [InlineData(ItsmRecordKind.Oco, "OCO-12345678901234567890", "OCO-12345678901234567890")]
    public void Number_ValidInput_IsCanonicalUpperCase(ItsmRecordKind kind, string input, string expected) =>
        ItsmRecordNumber.Normalize(kind, input).Should().Be(expected);

    [Theory]
    [InlineData(ItsmRecordKind.Or, "OR000456")]
    [InlineData(ItsmRecordKind.Or, "OR-")]
    [InlineData(ItsmRecordKind.Or, "OR--1")]
    [InlineData(ItsmRecordKind.Or, "OR-1a")]
    [InlineData(ItsmRecordKind.Or, "OR-+1")]
    [InlineData(ItsmRecordKind.Or, "OR-1\n2")]
    [InlineData(ItsmRecordKind.Or, "OR-１")]
    [InlineData(ItsmRecordKind.Oco, "OCO-123456789012345678901")]
    [InlineData(ItsmRecordKind.Oco, "OCO-1'")]
    [InlineData(ItsmRecordKind.Oco, "XOCO-1")]
    [InlineData(ItsmRecordKind.Oco, null)]
    public void Number_AnythingElse_IsRejected(ItsmRecordKind kind, string? input) =>
        ItsmRecordNumber.Normalize(kind, input).Should().BeNull();

    [Fact]
    public void Number_UndefinedKind_IsRejected() =>
        ItsmRecordNumber.Normalize((ItsmRecordKind)42, "OR-1").Should().BeNull();

    [Theory]
    [InlineData(ItsmRecordKind.Or, "OR-000001", ItsmRecordLookupStatus.Found, ItsmRecordLookupReason.None)]
    [InlineData(ItsmRecordKind.Oco, "oco-000001", ItsmRecordLookupStatus.Found, ItsmRecordLookupReason.None)]
    [InlineData(ItsmRecordKind.Or, "OR-000002", ItsmRecordLookupStatus.Ambiguous, ItsmRecordLookupReason.None)]
    [InlineData(ItsmRecordKind.Oco, "OCO-000002", ItsmRecordLookupStatus.Ambiguous, ItsmRecordLookupReason.None)]
    [InlineData(ItsmRecordKind.Or, "OR-000003", ItsmRecordLookupStatus.Unknown, ItsmRecordLookupReason.Unavailable)]
    [InlineData(ItsmRecordKind.Oco, "OCO-000003", ItsmRecordLookupStatus.Unknown, ItsmRecordLookupReason.Unavailable)]
    [InlineData(ItsmRecordKind.Oco, "OCO-000404", ItsmRecordLookupStatus.NotFound, ItsmRecordLookupReason.None)]
    [InlineData(ItsmRecordKind.Oco, "OCO-1'", ItsmRecordLookupStatus.Unknown, ItsmRecordLookupReason.InvalidNumber)]
    public async Task Fake_IsDeterministic_AndCoversAllFourStates(
        ItsmRecordKind kind, string number, ItsmRecordLookupStatus status, ItsmRecordLookupReason reason)
    {
        FakeItsmRecordLookup fake = new();

        ItsmRecordLookupResult first = await fake.LookupAsync(kind, number, CancellationToken.None);
        ItsmRecordLookupResult second = await fake.LookupAsync(kind, number, CancellationToken.None);

        first.Status.Should().Be(status);
        first.Reason.Should().Be(reason);
        first.Provider.Should().Be("Fake");
        second.Should().Be(first);
        (first.Record is not null).Should().Be(status == ItsmRecordLookupStatus.Found);
    }

    [Fact]
    public async Task Fake_FoundOco_CarriesRawCodesAndWindow_AndOrHasNone()
    {
        FakeItsmRecordLookup fake = new();

        ItsmRecord oco = (await fake.LookupAsync(ItsmRecordKind.Oco, "OCO-000001", CancellationToken.None)).Record!;
        ItsmRecord or = (await fake.LookupAsync(ItsmRecordKind.Or, "OR-000001", CancellationToken.None)).Record!;

        oco.RawStatusCode.Should().NotBeNull();
        oco.ProposedStartText.Should().NotBeNull();
        or.RawStatusCode.Should().BeNull();
        or.ProposedStartText.Should().BeNull();
    }

    [Fact]
    public async Task Disabled_IsAlwaysUnknownNotConfigured()
    {
        ItsmRecordLookupResult result = await new DisabledItsmRecordLookup()
            .LookupAsync(ItsmRecordKind.Oco, "OCO-000001", CancellationToken.None);

        result.Status.Should().Be(ItsmRecordLookupStatus.Unknown);
        result.Reason.Should().Be(ItsmRecordLookupReason.NotConfigured);
        result.Provider.Should().Be("Disabled");
    }

    [Fact]
    public void Registration_Default_IsDisabled() =>
        Resolve(new Dictionary<string, string?>(), "Production").Should().BeOfType<DisabledItsmRecordLookup>();

    [Theory]
    [InlineData("Development")]
    [InlineData("Demo")]
    [InlineData("Test")]
    public void Registration_Fake_InSyntheticEnvironment_IsFake(string environment) =>
        Resolve(new() { ["ItsmRecordLookup:Provider"] = "fake" }, environment).Should().BeOfType<FakeItsmRecordLookup>();

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Validation_Fake_OutsideSyntheticEnvironment_FailsAtStartup(string environment)
    {
        Action act = () => ItsmRecordLookupRegistration.Validate(Configuration(new() { ["ItsmRecordLookup:Provider"] = "Fake" }), environment);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Fake is permitted only*");
    }

    [Theory]
    [InlineData("Simulation")]
    [InlineData("Turuncu")]
    [InlineData("")]
    public void Validation_UnknownProvider_FailsInsteadOfFallingBack(string provider)
    {
        Action act = () => ItsmRecordLookupRegistration.Validate(Configuration(new() { ["ItsmRecordLookup:Provider"] = provider }), "Test");

        act.Should().Throw<InvalidOperationException>().WithMessage("*must be Disabled, Fake, or TuruncuHat*");
    }

    [Fact]
    public void Registration_TuruncuHat_WithCompleteReadConfiguration_IsTheRealAdapter() =>
        Resolve(TuruncuHat(), "Production").Should().BeOfType<TuruncuHatItsmRecordLookup>();

    [Theory]
    [InlineData("ItsmRecordLookup:OcoBaseObject", "")]
    [InlineData("ItsmRecordLookup:OrBaseObject", "bad name")]
    [InlineData("TuruncuHat:BaseUrl", "http://itsm.invalid/")]
    [InlineData("TuruncuHat:BaseUrl", "https://user:pass@itsm.invalid/")]
    [InlineData("TuruncuHat:Password", "")]
    [InlineData("TuruncuHat:Authorization", "Basic a\nb")]
    [InlineData("TuruncuHat:TenantId", "0")]
    [InlineData("TuruncuHat:SessionLifetimeSeconds", "0")]
    [InlineData("TuruncuHat:MaxResponseBytes", "10")]
    public void Validation_TuruncuHat_IncompleteReadConfiguration_FailsAtStartup(string key, string value)
    {
        Dictionary<string, string?> settings = TuruncuHat();
        settings[key] = value;

        Action act = () => ItsmRecordLookupRegistration.Validate(Configuration(settings), "Production");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validation_TuruncuHat_DoesNotRequireTheOperationalRecordImportMapping()
    {
        Dictionary<string, string?> settings = TuruncuHat();

        ItsmRecordLookupRegistration.Validate(Configuration(settings), "Production").Should().Be("TuruncuHat");
        settings.Keys.Should().NotContain(["TuruncuHat:SourceBaseObject", "TuruncuHat:RelatedGroupId"]);
    }

    private static IItsmRecordLookup Resolve(Dictionary<string, string?> settings, string environment)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSecureOpsInfrastructure(Configuration(settings), environment);
        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IItsmRecordLookup>();
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
