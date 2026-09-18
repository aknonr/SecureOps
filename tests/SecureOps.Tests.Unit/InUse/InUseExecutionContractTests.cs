using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SecureOps.Infrastructure.InUse;
using SecureOps.Infrastructure.InUse.Execution;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Unit.InUse;

public sealed class InUseExecutionContractTests
{
    [Theory]
    [InlineData(200, "Acknowledged")]
    [InlineData(401, "Unknown")]
    [InlineData(503, "Unknown")]
    [InlineData(0, "Unknown")]
    public async Task MutationAdapter_ReusesSessionAndExactBytes_NeverReplaysRejectedOrLostResponse(int status, string expected)
    {
        InUseSource source = (await new LocalInUseSourceClient().DiscoverAsync(CancellationToken.None)).Records[0]
            with
        { Id = "000123", Code = "OR-000123", Synthetic = false };
        byte[] bytes = [3, 1, 4, 1, 5];
        InUseExecutionIntent intent = new(Guid.NewGuid(), Guid.NewGuid(), 4, 2, "source", Convert.ToHexString(SHA256.HashData(bytes)),
            "archive.xlsx", Guid.NewGuid(), "Synthetic", DateTimeOffset.UtcNow, "configuration", "review", source);
        InUseExecutionLease lease = new(intent, bytes, Guid.NewGuid(), 2, 3,
            [new("Validate", "Verified", "synthetic-task", "test-only-target", DateTimeOffset.UtcNow, "test")]);
        ITuruncuHatSessionManager sessions = Substitute.For<ITuruncuHatSessionManager>();
        sessions.GetSessionAsync(Arg.Any<CancellationToken>()).Returns("synthetic-session");
        var handler = new MutationHandler(status);
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("TuruncuHat").Returns(_ => new HttpClient(handler, false));
        IOptions<OperationalRecordsOptions> writes = Options.Create(new OperationalRecordsOptions { ReadOnlyIntegrationMode = false, ControlledTestWritesEnabled = true, SourceCloseEnabled = true });
        var client = new TuruncuHatInUseMutationClient(factory, sessions, Options.Create(new TuruncuHatOptions
        { BaseUrl = "https://source.example.invalid/ws/DataRest.svc/json", Authorization = "Basic synthetic", TenantId = 218 }),
            writes, Options.Create(new InUseCompletionOptions { Enabled = true, Provider = "TuruncuHat" }));
        (await client.SendAsync("Upload", lease, null, null, null, default)).Outcome.Should().Be(expected);
        handler.Count.Should().Be(1);
        handler.Target.Should().Be("https://source.example.invalid/ws/DataRestSecure.svc/json/uploadattachment");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("req").GetProperty("datastring").GetBytesFromBase64().Should().Equal(bytes);
        body.RootElement.GetProperty("req").GetProperty("SessionID").GetString().Should().Be("synthetic-session");
        if (status == 401)
        { sessions.Received(1).Invalidate("synthetic-session"); }
        writes.Value.ReadOnlyIntegrationMode = true;
        (await client.SendAsync("Upload", lease, null, null, null, default)).Outcome.Should().Be("Rejected");
        handler.Count.Should().Be(1);
    }

    private sealed class MutationHandler(int status) : HttpMessageHandler
    {
        public int Count { get; private set; }
        public string? Target { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            Target = request.RequestUri!.AbsoluteUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (status == 0)
            { throw new HttpRequestException("Synthetic response loss; no live HTTP."); }
            return new((System.Net.HttpStatusCode)status) { Content = new StringContent("{\"UploadAttachmentStringResult\":{\"Success\":true}}") };
        }
    }

    [Theory]
    [InlineData("0002915", "00112", true)]
    [InlineData("2916", "00112", false)]
    [InlineData("0002915", "1E+06", false)]
    public void Aspect_RequiresExactPerServiceRelationship_PreservesText(string service, string id, bool expected)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            QueryResult = new
            {
                Items = new[] { new[] {
            new { Key = "SET.id", Value = id }, new { Key = "SET.p_name", Value = "[Genel]" },
            new { Key = "SET.p_rel_service_id", Value = service } } }
            }
        }));
        IReadOnlyDictionary<string, InUseEvidence> result = InUseAspectParser.Parse(json.RootElement, "0002915");
        result["ITMC_Servis_Unsuru_ID"].Value.Should().Be(expected ? id : null);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Aspect_MissingOrAmbiguous_NeverTakesFirst(int count)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { QueryResult = new { Items = Enumerable.Range(0, count).Select(_ => Array.Empty<object>()) } }));
        InUseAspectParser.Parse(json.RootElement, "2915")["ITMC_Servis_Unsuru_ID"].Value.Should().BeNull();
    }

    [Theory]
    [InlineData("{\"Success\":true}", "Acknowledged")]
    [InlineData("{}", "Unknown")]
    [InlineData("{\"Success\":false}", "Unknown")]
    [InlineData("{\"Success\":true,\"ErrorDetails\":\"private source failure\"}", "Unknown")]
    public void AttachmentResponse_IsNotClosureOrReadback_AndDoesNotExposeSourceErrors(string body, string outcome)
    {
        using var json = JsonDocument.Parse("{\"UploadAttachmentStringResult\":" + body + "}");
        InUseRemoteResult result = TuruncuHatInUseWireContract.Acknowledgement(json.RootElement, true);
        result.Outcome.Should().Be(outcome);
        result.Code.Should().NotContain("private");
    }

    [Fact]
    public async Task Wire_UsesExactArchivedBytesAndOr_NoCrossOriginOrUnverifiedEnvironment()
    {
        InUseSource source = (await new LocalInUseSourceClient().DiscoverAsync(CancellationToken.None)).Records[0] with { Id = "000123", Code = "OR-000123" };
        byte[] bytes = [1, 3, 7, 9];
        InUseExecutionIntent intent = new(Guid.NewGuid(), Guid.NewGuid(), 4, 2, "source", Convert.ToHexString(SHA256.HashData(bytes)),
            "archive-name.xlsx", Guid.NewGuid(), "Synthetic", DateTimeOffset.UtcNow, "configuration", "review", source);
        InUseExecutionLease lease = new(intent, bytes, Guid.NewGuid(), 2, 3, []);
        Uri standard = new("https://source.example.invalid/turuncuhat/ws/DataRest.svc/json/");
        Uri secure = new("https://source.example.invalid/turuncuhat/ws/DataRestSecure.svc/json/uploadattachment");
        using HttpRequestMessage request = TuruncuHatInUseWireContract.Attachment(standard, secure, lease, "synthetic-session", 218, "Basic synthetic");
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        JsonElement req = body.RootElement.GetProperty("req");
        req.GetProperty("fId").GetString().Should().Be("000123");
        req.GetProperty("fName").GetString().Should().Be("OR-000123_InUse.xlsx");
        req.GetProperty("datastring").GetBytesFromBase64().Should().Equal(bytes);
        Action wrong = () => TuruncuHatInUseWireContract.Attachment(standard, new("https://other.example.invalid/DataRestSecure.svc/json/uploadattachment"), lease, "session", 218, "Basic synthetic");
        wrong.Should().Throw<InvalidDataException>();
        Action mixed = () => TuruncuHatInUseWireContract.Property("121", 4464, "Mixed", "session", 218, "Basic synthetic");
        mixed.Should().Throw<InvalidDataException>();
        using HttpRequestMessage bpm = TuruncuHatInUseWireContract.Activity("567", source.Id, "session", 218, "Basic synthetic");
        (await bpm.Content!.ReadAsStringAsync()).Should().Contain("103626").And.Contain("103627").And.Contain("000123").And.Contain("567");
    }

    [Fact]
    public async Task CompletionPolicy_FlagsDoNotEnableUnknownCorporateContracts_OrSyntheticProviderInProduction()
    {
        InUseSource source = (await new LocalInUseSourceClient().DiscoverAsync(CancellationToken.None)).Records[0];
        IOptions<OperationalRecordsOptions> writes = Options.Create(new OperationalRecordsOptions { ReadOnlyIntegrationMode = false, ControlledTestWritesEnabled = true, SourceCloseEnabled = true });
        IOptions<InUseCompletionOptions> options = Options.Create(new InUseCompletionOptions { Enabled = true, Provider = "TuruncuHat" });
        new InUseCompletionPolicy(options, writes, "Test").Readiness(source with { Synthetic = false }).Code.Should().Be("SourceContractsMissing");
        options.Value.Provider = "Fixture";
        options.Value.FixtureDirectory = Path.GetTempPath();
        new InUseCompletionPolicy(options, writes, "Production").Readiness(source).Available.Should().BeFalse();
        new InUseCompletionPolicy(options, writes, "Test").Readiness(source with { Synthetic = false }).Available.Should().BeFalse();
        new InUseCompletionPolicy(options, writes, "Test").Readiness(source).Available.Should().BeTrue();
    }
}
