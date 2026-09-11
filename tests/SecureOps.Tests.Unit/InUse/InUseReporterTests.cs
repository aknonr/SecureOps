using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using SecureOps.Infrastructure.InUse;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Unit.InUse;

public sealed partial class InUseTests
{
    [Fact]
    public async Task Reporter_StoredPerServer_PreservesReviewerAndVersionsUntilContentChanges()
    {
        var f = new Fixture();
        InUseRecord record = await f.ImportAsync();
        record = (await f.Service.AssignAsync(_principal, _context, record.Id, new(record.Version, f.User.Id, "Manual"), _token)).Value!;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        InUseSource source = record.Source with
        {
            ServiceItemsState = "Observed",
            Servers = record.Source.Servers.Select((s, i) => s with
            {
                RelatedRequestReporter = new(record.Source.Id, s.Id, $"OR-{200 + i}", "OrCode", $"{200 + i}",
                    $"OR-{200 + i}", $"Synthetic reporter {i}", $"{800 + i}", "ExactMatch", "Returned", "Returned", now)
            }).ToArray()
        };
        async Task<InUseRecord> Refresh(InUseSource next)
        {
            f.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(new InUseBatch([next], false));
            (await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token)).Error.Should().BeNull();
            return (await f.Repository.GetAsync(record.Id, _token))!;
        }
        record = await Refresh(source);
        record.Source.Servers.Select(s => s.RelatedRequestReporter!.Display).Distinct().Should().HaveCount(source.Servers.Count);
        InUseRecord restored = JsonSerializer.Deserialize<InUseRecord>(JsonSerializer.Serialize(record))!;
        restored.Should().BeEquivalentTo(record);
        string hash = record.SourceHash;
        long version = record.SourceVersion;
        source = source with
        {
            Servers = source.Servers.Select(s => s with
            { RelatedRequestReporter = s.RelatedRequestReporter! with { LastVerifiedAt = now.AddMinutes(1) } }).ToArray()
        };
        record = await Refresh(source);
        record.SourceHash.Should().Be(hash);
        record.SourceVersion.Should().Be(version);
        record.AssigneeId.Should().Be(f.User.Id);
        InUseSource failed = source with
        {
            Servers = source.Servers.Select(s => s with
            { RelatedRequestReporter = s.RelatedRequestReporter! with { State = "Forbidden", Display = null, UserReference = null, LastVerifiedAt = null } }).ToArray()
        };
        record = await Refresh(failed);
        record.Source.Servers.Should().OnlyContain(s => s.RelatedRequestReporter!.State == "Forbidden"
            && s.RelatedRequestReporter.Display != null && s.RelatedRequestReporter.LastVerifiedAt == now.AddMinutes(1));
        record.Source.Servers[0].RelatedRequestReporter!.DisplayText(now.AddMinutes(1)).Should().Be("Forbidden");
        source = source with { Servers = source.Servers.Select(s => s with { RelatedRequestReporter = null }).ToArray() };
        record = await Refresh(source);
        record.Source.Servers.Should().OnlyContain(s => s.RelatedRequestReporter!.State == "Stale");
        record.SourceVersion.Should().Be(version + 2);
        record.AssigneeId.Should().Be(f.User.Id);
        record.Source.Requester.Should().Be(source.Requester);
    }
}

public sealed class InUseRelatedRequestParserTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactClosedRequest_ReorderedCells_SeparatesRawDisplayAndNumericReference(bool reverse)
    {
        object[] cells = [new { Key = "SET.id", Value = (object)200 }, new { Key = "SET.p_code", Value = (object)"OR-200" },
            new { Key = "KEY.p_rel_requester", Value = (object)"Sentetik &#350;ah&#305;s &amp;lt;b&amp;gt;" },
            new { Key = "SET.p_rel_requester", Value = (object)800 }];
        if (reverse)
        { Array.Reverse(cells); }
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(new { QueryResult = new { Items = new[] { cells } } }));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        InUseRelatedRequestReporter value = InUseRelatedRequestParser.Parse("100", "1001", "OR-200",
            new("c_rfc_record", "SET", "OrCode"), response.RootElement, now);
        value.State.Should().Be("ExactMatch");
        value.ParentId.Should().Be("100");
        value.ServiceItemId.Should().Be("1001");
        value.UserReference.Should().Be("800");
        value.Display.Should().Contain("&#350;");
        value.DisplayText(now).Should().Be("Sentetik \u015eah\u0131s &lt;b&gt;");
        value.EffectiveState(now.AddHours(25)).Should().Be("Stale");
        value.EffectiveState(now.AddMinutes(-1)).Should().Be("Stale");
        (value with { State = "Forbidden" }).DisplayText(now).Should().Be("Forbidden");
    }

    [Theory]
    [InlineData("200", "OR-201", "OrCode")]
    [InlineData("0200", "OR-200", "SourceId")]
    [InlineData("200", "", "SourceId")]
    public void IdentityOrCodeMismatch_FailsClosed(string id, string code, string kind)
    {
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            QueryResult = new
            {
                Items = new[] {
            new[] { new { Key = "SET.id", Value = id }, new { Key = "SET.p_code", Value = code } } }
            }
        }));
        InUseRelatedRequestParser.Parse("100", "1001", kind == "OrCode" ? "OR-200" : "200",
            new("c_rfc_record", "SET", kind), response.RootElement, DateTimeOffset.UtcNow).State.Should().Be("IdentityMismatch");
    }
}
