using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SecureOps.Infrastructure.InUse;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Unit.InUse;

public sealed partial class InUseAdapterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refresh_UsesEachRfc_DeduplicatesClosedTargets_WithoutVirtualPcUser(bool reverse)
    {
        var rows = new[] { "OR-200", "OR-200", "OR-201", null }.Select((r, i) => new[] {
            new { Key = "SET.(LCSIMS_ServiceInstance)m_rid.c_rfc_record", Value = r },
            new { Key = "SET.(LCSIMS_ServiceInstance)m_rid.id", Value = (string?)(1001 + i).ToString() } }).ToArray();
        if (reverse)
        { Array.Reverse(rows); foreach (var row in rows) { Array.Reverse(row); } }
        using var handler = new Handler("{\"QueryResult\":{\"Items\":[" + _row + "]}}",
            JsonSerializer.Serialize(new { QueryResult = new { Items = rows } }), referenced: "exact-fixture");
        InUseBatch batch = await Client(handler).DiscoverAsync(CancellationToken.None);
        IReadOnlyList<InUseServer> servers = batch.Records.Single().Servers;
        servers.Select(s => s.RelatedRequestReporter!.RfcReference).Should().Equal("OR-200", "OR-200", "OR-201", null);
        servers.Take(3).Should().OnlyContain(s => s.RelatedRequestReporter!.State == "ExactMatch"
            && s.RelatedRequestReporter.ParentId == "100" && s.RelatedRequestReporter.ServiceItemId == s.Id);
        servers[0].RelatedRequestReporter!.Display.Should().Be("Sentetik &#350;ah&#305;s 200 &lt;b&gt;");
        servers[2].RelatedRequestReporter!.Display.Should().Contain("201");
        servers[0].RelatedRequestReporter!.UserReference.Should().Be("800");
        servers[3].RelatedRequestReporter!.State.Should().Be("MissingRfc");
        handler.Requests.Should().HaveCount(4);
        handler.Requests.Skip(2).Select(r => r.Filter).Should().Equal("#%p_code%#='OR-200'", "#%p_code%#='OR-201'");
        handler.Selects[1].Should().HaveCount(16).And.NotContain(s => s.Contains("virtual_pc", StringComparison.Ordinal));
        handler.Selects.Skip(2).Should().OnlyContain(s => s.SequenceEqual(new[] { "id", "p_code", "p_rel_requester" }));
    }

    [Theory]
    [InlineData("{\"QueryResult\":{\"Items\":[]}}", 200, "NotFoundOrNotVisible")]
    [InlineData("{\"QueryResult\":{\"Items\":[[],[]]}}", 200, "AmbiguousMatch")]
    [InlineData("{}", 403, "Forbidden")]
    [InlineData("{}", 500, "Failed")]
    public async Task Refresh_TargetFailure_PreservesFourIndependentLinks(string target, int status, string state)
    {
        var rows = Enumerable.Range(1001, 4).Select(i => new[] {
            new { Key = "SET.(LCSIMS_ServiceInstance)m_rid.id", Value = i.ToString() },
            new { Key = "SET.(LCSIMS_ServiceInstance)m_rid.c_rfc_record", Value = "OR-200" } });
        using var handler = new Handler("{\"QueryResult\":{\"Items\":[" + _row + "]}}",
            JsonSerializer.Serialize(new { QueryResult = new { Items = rows } }), referenced: target, referencedStatus: (HttpStatusCode)status);
        InUseBatch result = await Client(handler).DiscoverAsync(CancellationToken.None);
        result.Records.Single().Servers.Should().HaveCount(4).And.OnlyContain(s => s.RelatedRequestReporter!.State == state);
        handler.Requests.Should().HaveCount(status == 403 ? 4 : 3);
    }

    [Fact]
    public async Task Refresh_DeduplicatesAcrossParents_AndCapsDistinctTargetReads()
    {
        static object Cell(string key, string? value) => new { Key = key, Value = value };
        string Response(string filter)
        {
            object[][] rows;
            if (filter.Contains("m_active", StringComparison.Ordinal))
            { rows = [JsonSerializer.Deserialize<object[]>(_row)!, JsonSerializer.Deserialize<object[]>(_row.Replace("100", "101", StringComparison.Ordinal))!]; }
            else if (filter.Contains("m_lid", StringComparison.Ordinal))
            {
                bool first = filter.EndsWith("=100", StringComparison.Ordinal);
                rows = Enumerable.Range(0, first ? 10 : 2).Select(i => new[] {
                    Cell("SET.(LCSIMS_ServiceInstance)m_rid.id", (first ? 1000 + i : 2000 + i).ToString()),
                    Cell("SET.(LCSIMS_ServiceInstance)m_rid.c_rfc_record", $"OR-{(first ? 200 + i : i == 0 ? 200 : 210)}") }).ToArray();
            }
            else
            { rows = []; }
            return JsonSerializer.Serialize(new { QueryResult = new { Items = rows } });
        }
        using var handler = new Handler("{}", responseForFilter: Response);
        InUseBatch batch = await Client(handler).DiscoverAsync(CancellationToken.None);
        batch.Records[1].Servers[0].RelatedRequestReporter!.State.Should().Be("NotFoundOrNotVisible");
        batch.Records[1].Servers[0].RelatedRequestReporter!.ParentId.Should().Be("101");
        batch.Records[1].Servers[1].RelatedRequestReporter!.State.Should().Be("NotQueried");
        handler.Requests.Count(r => r.Filter.StartsWith("#%p_code%#", StringComparison.Ordinal)).Should().Be(10);
    }

    [Fact]
    public void DiagnosticJson_ReproducesUtf8BomFailure_ThenParsesWithoutChangingHashInput()
    {
        byte[] plain = Encoding.UTF8.GetBytes("{\"RFC Kayd\u0131\":\"c_rfc_record\"}");
        byte[] bom = [0xef, 0xbb, 0xbf, .. plain];
        byte[] original = bom.ToArray();
        Action previous = () => JsonSerializer.Deserialize<Dictionary<string, string>>(bom);
        previous.Should().Throw<JsonException>();
        InUseDiagnosticJson.Read<Dictionary<string, string>>(bom).Should()
            .BeEquivalentTo(InUseDiagnosticJson.Read<Dictionary<string, string>>(plain));
        bom.Should().Equal(original);
        byte[] contract = [0xef, 0xbb, 0xbf, .. JsonSerializer.SerializeToUtf8Bytes(new InUseReferencedRequestContract("c_rfc_record", "SET", "OrCode"))];
        InUseDiagnosticJson.Read<InUseReferencedRequestContract>(contract).Validate(new Dictionary<string, string> { ["RFC Kayd\u0131"] = "c_rfc_record" });
        FluentActions.Invoking(() => InUseDiagnosticJson.Read<object>(new byte[] { 0xef, 0xbb, 0xbf, 0x7b })).Should().Throw<JsonException>();
        FluentActions.Invoking(() => InUseDiagnosticJson.Read<object>(new byte[4097])).Should().Throw<InvalidDataException>();
    }
}
