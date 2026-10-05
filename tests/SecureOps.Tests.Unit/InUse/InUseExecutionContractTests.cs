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
    [InlineData("Upload", 200, "Acknowledged")]
    [InlineData("Upload", 401, "Unknown")]
    [InlineData("Upload", 503, "Unknown")]
    [InlineData("Upload", 0, "Unknown")]
    [InlineData("Bpm", 200, "Acknowledged")]
    [InlineData("Bpm", 201, "Rejected")]
    [InlineData("Bpm", 503, "Unknown")]
    [InlineData("Bpm", 0, "Unknown")]
    public async Task MutationAdapter_ReusesSessionAndExactBytes_NeverReplaysRejectedOrLostResponse(string step, int status, string expected)
    {
        InUseSource source = (await new LocalInUseSourceClient().DiscoverAsync(CancellationToken.None)).Records[0]
            with
        { Id = "000123", Code = "OR-000123", Synthetic = false };
        byte[] bytes = [3, 1, 4, 1, 5];
        InUseExecutionIntent intent = new(Guid.NewGuid(), Guid.NewGuid(), 4, 2, "source", Convert.ToHexString(SHA256.HashData(bytes)),
            "archive.xlsx", Guid.NewGuid(), "Synthetic", DateTimeOffset.UtcNow, "configuration", "review", source);
        InUseExecutionLease lease = new(intent, bytes, Guid.NewGuid(), 2, step == "Bpm" ? 5 : 3,
            [new("Validate", "Verified", "synthetic-task", "test-only-target", DateTimeOffset.UtcNow, "test"),
             new("Attachment", "Verified", "456", "exact-reviewed-bytes", DateTimeOffset.UtcNow, "test")]);
        ITuruncuHatSessionManager sessions = Substitute.For<ITuruncuHatSessionManager>();
        sessions.GetSessionAsync(Arg.Any<CancellationToken>()).Returns("synthetic-session");
        var handler = new MutationHandler(status);
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("TuruncuHat").Returns(_ => new HttpClient(handler, false));
        IOptions<OperationalRecordsOptions> writes = Options.Create(new OperationalRecordsOptions { ReadOnlyIntegrationMode = false, ControlledTestWritesEnabled = true, SourceCloseEnabled = true });
        var client = new TuruncuHatInUseMutationClient(factory, sessions, Options.Create(new TuruncuHatOptions
        { BaseUrl = "https://source.example.invalid/ws/DataRest.svc/json", Authorization = "Basic synthetic", TenantId = 218 }),
            writes, Options.Create(new InUseCompletionOptions { Enabled = true, Provider = "TuruncuHat" }));
        (await client.SendAsync(step, lease, null, "789", null, TestContext.Current.CancellationToken)).Outcome.Should().Be(expected);
        handler.Count.Should().Be(1);
        using var body = JsonDocument.Parse(handler.Body!);
        if (step == "Upload")
        {
            handler.Target.Should().Be("https://source.example.invalid/ws/DataRestSecure.svc/json/uploadattachment");
            body.RootElement.GetProperty("req").GetProperty("datastring").GetBytesFromBase64().Should().Equal(bytes);
        }
        else
        {
            handler.Target.Should().Be("https://source.example.invalid/ws/DataRest.svc/json/update");
            body.RootElement.GetProperty("req").GetProperty("BaseObject").GetString().Should().Be("BPM_Actvty");
            body.RootElement.GetProperty("req").GetProperty("Filters")[0].GetString().Should().Contain("789").And.Contain("000123").And.Contain("m_status%#=1");
            (await client.SendAsync(step, lease with { Evidence = lease.Evidence.Where(e => e.Step != "Attachment").ToArray() }, null, "789", null, TestContext.Current.CancellationToken))
                .Code.Should().Be("VerifiedPreconditionsRequired");
            handler.Count.Should().Be(1);
        }
        body.RootElement.GetProperty("req").GetProperty("SessionID").GetString().Should().Be("synthetic-session");
        if (status == 401)
        { sessions.Received(1).Invalidate("synthetic-session"); }
        writes.Value.ReadOnlyIntegrationMode = true;
        (await client.SendAsync(step, lease, null, "789", null, TestContext.Current.CancellationToken)).Outcome.Should().Be("Rejected");
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
            string root = request.RequestUri.AbsolutePath.EndsWith("uploadattachment", StringComparison.Ordinal) ? "UploadAttachmentStringResult" : "UpdateResult";
            return new((System.Net.HttpStatusCode)status) { Content = new StringContent("{\"" + root + "\":{\"Success\":" + (status == 201 ? "false" : "true") + "}}") };
        }
    }

    [Theory]
    [InlineData("DEV", "UAT", "TEST", "Acknowledged")]
    [InlineData("PROD", "DEV", "PROD", "Acknowledged")]
    [InlineData("DEV", "", "TEST", "Rejected")]
    [InlineData("DEV", "UAT", "PROD", "Rejected")]
    public async Task EnvironmentProperty_UsesReviewedOrWideRule_NotRawLastServerValue(
        string first, string second, string proposed, string expected)
    {
        InUseSource seed = (await new LocalInUseSourceClient().DiscoverAsync(CancellationToken.None)).Records[0];
        InUseSource source = seed with
        {
            Id = "000123",
            Code = "OR-000123",
            Synthetic = false,
            Servers = [new("first", new Dictionary<string, InUseEvidence>
                { ["SI_ENVIRONMENT"] = new(first, "synthetic source") }),
                new("second", new Dictionary<string, InUseEvidence>
                { ["SI_ENVIRONMENT"] = new(second, "synthetic source") })]
        };
        byte[] bytes = [1, 2, 3];
        InUseExecutionIntent intent = new(Guid.NewGuid(), Guid.NewGuid(), 4, 2, "source",
            Convert.ToHexString(SHA256.HashData(bytes)), "review.xlsx", Guid.NewGuid(), "Synthetic",
            DateTimeOffset.UtcNow, "configuration", "review", source);
        InUseExecutionLease lease = new(intent, bytes, Guid.NewGuid(), 2, 2,
            [new("Validate", "Verified", "synthetic-task", "synthetic evidence", DateTimeOffset.UtcNow, "test")]);
        ITuruncuHatSessionManager sessions = Substitute.For<ITuruncuHatSessionManager>();
        sessions.GetSessionAsync(Arg.Any<CancellationToken>()).Returns("synthetic-session");
        var handler = new MutationHandler(200);
        IHttpClientFactory clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient("TuruncuHat").Returns(_ => new HttpClient(handler, false));
        var client = new TuruncuHatInUseMutationClient(clients, sessions,
            Options.Create(new TuruncuHatOptions
            {
                BaseUrl = "https://source.example.invalid/ws/DataRest.svc/json",
                Authorization = "synthetic",
                TenantId = 1
            }),
            Options.Create(new OperationalRecordsOptions
            {
                ReadOnlyIntegrationMode = false,
                ControlledTestWritesEnabled = true,
                SourceCloseEnabled = true
            }),
            Options.Create(new InUseCompletionOptions { Enabled = true, Provider = "TuruncuHat" }));

        InUseRemoteResult result = await client.SendAsync("Property4464", lease, "500", null, proposed, TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(expected);
        if (expected == "Rejected")
        { handler.Count.Should().Be(0); return; }
        handler.Count.Should().Be(1);
        using var json = JsonDocument.Parse(handler.Body!);
        JsonElement request = json.RootElement.GetProperty("req");
        request.GetProperty("BaseObject").GetString().Should().Be("DCM_DynamicCaseProperty");
        request.GetProperty("Filters")[0].GetString().Should().Contain("500").And.Contain("4464");
        request.GetProperty("Updates").EnumerateArray().Select(v => v.GetString()).Should().Equal("p_value", proposed);
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
    [InlineData("{\"Success\":false}", "Rejected")]
    [InlineData("{\"Success\":true,\"ErrorNo\":12}", "Unknown")]
    [InlineData("{\"Success\":true,\"ErrorNo\":\"12\"}", "Unknown")]
    [InlineData("{\"Success\":true,\"ErrorNo\":{}}", "Unknown")]
    [InlineData("{\"Success\":true,\"ErrorNo\":\"invalid\"}", "Unknown")]
    [InlineData("{\"Success\":true,\"ErrorNo\":0}", "Acknowledged")]
    [InlineData("{\"Success\":true,\"ErrorNo\":\"0\"}", "Acknowledged")]
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
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
        JsonElement req = body.RootElement.GetProperty("req");
        req.GetProperty("fId").GetString().Should().Be("000123");
        req.GetProperty("fName").GetString().Should().Be("OR-000123_InUse.xlsx");
        req.GetProperty("datastring").GetBytesFromBase64().Should().Equal(bytes);
        Action wrong = () => TuruncuHatInUseWireContract.Attachment(standard, new("https://other.example.invalid/DataRestSecure.svc/json/uploadattachment"), lease, "session", 218, "Basic synthetic");
        wrong.Should().Throw<InvalidDataException>();
        Action mixed = () => TuruncuHatInUseWireContract.Property("121", 4464, "Mixed", "session", 218, "Basic synthetic");
        mixed.Should().Throw<InvalidDataException>();
        using HttpRequestMessage bpm = TuruncuHatInUseWireContract.Activity("567", source.Id, "session", 218, "Basic synthetic");
        (await bpm.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("103626").And.Contain("103627").And.Contain("000123").And.Contain("567");
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
