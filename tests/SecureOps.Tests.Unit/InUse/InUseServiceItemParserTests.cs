using System.Text.Json;
using FluentAssertions;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Unit.InUse;

public sealed class InUseServiceItemParserTests
{
    private const string _prefix = "(LCSIMS_ServiceInstance)m_rid.";
    internal static List<Dictionary<string, string?>> Row(int index)
    {
        var cells = new List<Dictionary<string, string?>>();
        string[] references = ["p_SI_def_server_type", "p_SI_def_environment", "p_rel_company_owner", "c_new_SI_major_project",
            "p_SI_def_network_segment", "p_SI_def_os_name", "p_def_os_version", "c_new_SI_major_project.p_rel_obs",
            "p_rel_asset_item.p_rel_lbs", "p_rel_asset_item.p_rel_lbs.m_parent", "p_def_category"];
        void Cell(string key, string value) => cells.Add(new() { ["Key"] = key, ["Value"] = value });
        Cell("SET." + _prefix + "id", (1000 + index).ToString());
        Cell("SET." + _prefix + "p_name", "synthetic-server-" + index);
        foreach (string property in references)
        {
            Cell("KEY." + _prefix + property, $"display-{index}-{property}");
            Cell("SET." + _prefix + property, property == "c_new_SI_major_project" ? (2000 + index).ToString() : $"reference-{index}-{property}");
        }
        Cell("SET." + _prefix + "p_SI_ip_SI_address_1", "192.0.2." + (index + 1));
        Cell("SET." + _prefix + "c_new_SI_major_project.id", (2000 + index).ToString());
        Cell("num", (index + 1).ToString());
        return cells;
    }
    internal static string Response(int count) => JsonSerializer.Serialize(new { QueryResult = new { Items = Enumerable.Range(0, count).Select(Row) } });
    private static IReadOnlyList<InUseServer> Parse(IEnumerable<IEnumerable<Dictionary<string, string?>>> rows)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { QueryResult = new { Items = rows } }));
        return InUseServiceItemParser.Parse(json.RootElement);
    }

    [Fact]
    public void Parse_Reordered27Cells_PreservesPerServerDisplayReferencesAndStableOrdering()
    {
        List<Dictionary<string, string?>>[] rows = Enumerable.Range(0, 4).Select(Row).ToArray();
        rows.Should().OnlyContain(row => row.Count == 27);
        IReadOnlyList<InUseServer> expected = Parse(rows);
        IReadOnlyList<InUseServer> reordered = Parse(rows.Reverse().Select(row => row.AsEnumerable().Reverse()));
        JsonSerializer.Serialize(reordered).Should().Be(JsonSerializer.Serialize(expected));
        expected.Select(s => s.Id).Should().Equal("1000", "1001", "1002", "1003");
        for (int i = 0; i < 4; i++)
        {
            expected[i].Fields["HOSTNAME"].Value.Should().Be("synthetic-server-" + i);
            expected[i].Fields["SI_ENVIRONMENT"].Value.Should().Be($"display-{i}-p_SI_def_environment");
            expected[i].Fields["ITMC_Service_ID"].Value.Should().Be((2000 + i).ToString());
            expected[i].Fields["Reference: SI_ENVIRONMENT"].Value.Should().Be($"reference-{i}-p_SI_def_environment");
            expected[i].Fields.Should().NotContainKey("Virtual PC User").And.NotContainKey("RFC Kaydı").And.NotContainKey("STATUS");
        }
    }

    [Theory]
    [InlineData("KEY.p_SI_def_environment", "SI_ENVIRONMENT")]
    [InlineData("SET.p_name", "HOSTNAME")]
    [InlineData("SET.c_new_SI_major_project.id", "ITMC_Service_ID")]
    public void Parse_MissingDisplayOrScalar_NeverSubstitutesAnotherCell(string suffix, string field)
    {
        List<Dictionary<string, string?>> row = Row(0);
        string key = suffix[..4] + _prefix + suffix[4..];
        row.RemoveAll(c => c["Key"] == key);
        InUseEvidence value = Parse([row]).Single().Fields[field];
        value.Value.Should().BeNull();
        value.Source.Should().Be("Missing response cell: " + key);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Parse_DuplicateKeyEvenWhenEqual_RejectsAmbiguity(bool conflict)
    {
        List<Dictionary<string, string?>> row = Row(0);
        row.Add(new(row[2]) { ["Value"] = conflict ? "conflicting" : row[2]["Value"] });
        FluentActions.Invoking(() => Parse([row])).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Parse_MissingDuplicateIdentityAndConflictingServiceReferences_Rejects()
    {
        List<Dictionary<string, string?>> row = Row(0);
        FluentActions.Invoking(() => Parse([row, row])).Should().Throw<InvalidDataException>();
        row.RemoveAt(0);
        FluentActions.Invoking(() => Parse([row])).Should().Throw<InvalidDataException>();
        row = Row(0);
        row.Single(c => c["Key"] == "SET." + _prefix + "c_new_SI_major_project.id")["Value"] = "9999";
        FluentActions.Invoking(() => Parse([row])).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void Parse_MalformedRow_Rejects(string row)
    {
        using var json = JsonDocument.Parse("{\"QueryResult\":{\"Items\":[" + row + "]}}");
        FluentActions.Invoking(() => InUseServiceItemParser.Parse(json.RootElement)).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Parse_EmptyAndOversizedResults_RespectBounds()
    {
        Parse([]).Should().BeEmpty();
        FluentActions.Invoking(() => Parse(Enumerable.Range(0, 11).Select(Row))).Should().Throw<InvalidDataException>();
        List<Dictionary<string, string?>> row = Row(0);
        row[1]["Value"] = new string('x', 1001);
        FluentActions.Invoking(() => Parse([row])).Should().Throw<InvalidDataException>();
    }
}
