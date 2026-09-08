using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SecureOps.Infrastructure.InUse;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.InUse;

public sealed class InUseAdapterTests
{
    private const string _row = """
        [{"Key":"KEY.p_rel_requester","Value":"Synthetic requester"},{"Key":"SET.p_description","Value":"Synthetic description"},
        {"Key":"SET.p_code","Value":"OR-100"},{"Key":"SET.p_rel_requester","Value":"888"},
        {"Key":"SET.p_name","Value":"Synthetic title"},{"Key":"num","Value":"1"},{"Key":"SET.id","Value":"100"}]
        """;

    [Fact]
    public async Task Discovery_ReorderedExpandedKeys_SeparateScopeAndUnresolvedRelationships()
    {
        using var handler = new Handler("{\"QueryResult\":{\"Items\":[" + _row + "]}}");
        TuruncuHatOperationalRecordClient client = Client(handler);
        InUseBatch result = await client.DiscoverAsync(CancellationToken.None);
        result.Complete.Should().BeFalse();
        result.Records.Single().Requester.Value.Should().Be("Synthetic requester");
        result.Records.Single().ServiceOwner.Value.Should().BeNull();
        result.Records.Single().Servers.Should().BeEmpty();
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Path.Should().Be("/query");
        handler.Requests[0].Filter.Should().Be("#%m_active%#='True' AND #%p_dcc%# IN (4241) AND #%p_rel_group%# IN (68)");
        await client.GetActiveAsync(100, CancellationToken.None);
        handler.Requests[1].Filter.Should().Contain("NOT IN").And.Contain("4241");
        handler.Requests.Should().OnlyContain(r => r.Path == "/query");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"QueryResult\":{\"ErrorNo\":1,\"Items\":[]}}")]
    [InlineData("{\"QueryResult\":{\"Items\":null}}")]
    [InlineData("{\"QueryResult\":{\"Items\":[[{\"Key\":\"SET.id\",\"Value\":\"100\"}]]}}")]
    [InlineData("{\"QueryResult\":{\"Items\":[[[{\"Value\":\"100\"}],[{\"Value\":\"OR-100\"}],[{\"Value\":\"title\"}],[{\"Value\":\"description\"}],[{\"Value\":\"requester\"}]]]}}")]
    public async Task Discovery_MalformedOrPositionalOnly_RejectsEntireBatch(string response)
    {
        using var handler = new Handler(response);
        Func<Task> read = () => Client(handler).DiscoverAsync(CancellationToken.None);
        await read.Should().ThrowAsync<InvalidDataException>();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Discovery_ZeroResults_IsNotProofOfCompleteness()
    {
        using var handler = new Handler("{\"QueryResult\":{\"Items\":[]}}");
        InUseBatch result = await Client(handler).DiscoverAsync(CancellationToken.None);
        result.Records.Should().BeEmpty();
        result.Complete.Should().BeFalse();
    }

    private static TuruncuHatOperationalRecordClient Client(Handler handler)
    {
        ITuruncuHatSessionManager sessions = Substitute.For<ITuruncuHatSessionManager>();
        sessions.GetSessionAsync(Arg.Any<CancellationToken>()).Returns("synthetic-session");
        return new(new HttpClient(handler) { BaseAddress = new Uri("https://source.invalid/") }, sessions,
            Options.Create(new TuruncuHatOptions { RelatedGroupId = 68, ExcludedDccIds = [4241] }), Options.Create(new OperationalRecordsOptions()),
            new EnterpriseIntegrationHealthState(), new EnterpriseIntegrationTelemetry(), NullLogger<TuruncuHatOperationalRecordClient>.Instance);
    }
    private sealed class Handler(string body) : HttpMessageHandler
    {
        public List<(string Path, string Filter)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Requests.Add((request.RequestUri!.AbsolutePath, json.RootElement.GetProperty("req").GetProperty("Filters")[0].GetString()!));
            return new(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
}
