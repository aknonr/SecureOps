using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Tests.Unit.ServiceAccounts;

public sealed class ChangePlanRulesTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid _a = Guid.Parse("0f000000-0000-4000-8000-00000000000a");
    private static readonly Guid _b = Guid.Parse("0f000000-0000-4000-8000-00000000000b");
    private static readonly Guid _link = Guid.Parse("0f000000-0000-4000-8000-0000000000aa");

    private static ChangePlanPreviewRow Row(Guid account, string server, string type, string name, string identity = @"SYN\svc_old",
        ChangePlanFlag flag = ChangePlanFlag.Ok) =>
        new(account, _link, server, type, name, identity, "gmsaSyn1", flag, _now.AddDays(-1));

    [Fact]
    public void PreviewDigest_IsCanonical_IndependentOfRowOrder()
    {
        ChangePlanPreviewRow[] rows =
        [
            Row(_a, "SYN-APP-01", "WindowsService", "SynService"),
            Row(_a, "SYN-APP-01", "ScheduledTask", @"\SynTask"),
            Row(_b, "SYN-WEB-02", "IisAppPool", "SynPool"),
            new(_b, null, null, null, null, null, "gmsaSyn2", ChangePlanFlag.NoScan, null)
        ];

        string digest = ChangePlanRules.Digest(rows);

        digest.Should().MatchRegex("^[0-9a-f]{64}$");
        ChangePlanRules.Digest(rows.Reverse()).Should().Be(digest);
        ChangePlanRules.Digest([rows[2], rows[0], rows[3], rows[1]]).Should().Be(digest);
        // The same instant written with another offset is the same row.
        ChangePlanRules.Digest(rows.Select(r => r with { ScanAt = r.ScanAt?.ToOffset(TimeSpan.FromHours(3)) })).Should().Be(digest);
    }

    [Fact]
    public void PreviewDigest_ChangesWhenAnyFieldChanges()
    {
        ChangePlanPreviewRow row = Row(_a, "SYN-APP-01", "WindowsService", "SynService");
        string digest = ChangePlanRules.Digest([row]);

        ChangePlanPreviewRow[] changed =
        [
            row with { AccountId = _b },
            row with { ScanLinkId = Guid.NewGuid() },
            row with { ScanLinkId = null },
            row with { ServerName = "SYN-APP-02" },
            row with { ComponentType = "ScheduledTask" },
            row with { ComponentName = "SynService2" },
            row with { CurrentIdentity = @"SYN\svc_other" },
            row with { CurrentIdentity = null },
            row with { TargetIdentity = "gmsaSyn9" },
            row with { Flag = ChangePlanFlag.StaleScan },
            row with { ScanAt = row.ScanAt!.Value.AddTicks(1) },
            row with { ScanAt = null }
        ];

        changed.Select(c => ChangePlanRules.Digest([c])).Should().OnlyHaveUniqueItems().And.NotContain(digest);
        // Field boundaries are unambiguous: moving characters between neighbouring fields changes the digest.
        ChangePlanRules.Digest([row with { ServerName = "SYN-APP-0", ComponentType = "1WindowsService" }]).Should().NotBe(digest);
        ChangePlanRules.Digest([row, row]).Should().NotBe(digest, "a duplicated row is a different preview");
    }

    [Fact]
    public void Preview_WithoutDiscoveryScan_IsOneNoInformationRow()
    {
        ChangePlanPreviewRow[] rows = [.. ChangePlanRules.BuildPreview([new ChangePlanPreviewAccount(_a, "gmsaSyn1", null)], _now)];

        rows.Should().ContainSingle().Which.Should().Be(new ChangePlanPreviewRow(_a, null, null, null, null, null, "gmsaSyn1", ChangePlanFlag.NoScan, null));
    }

    [Fact]
    public void Preview_FlagsStaleScans_UncoveredServers_AndManualOnlyComponents_WithoutBlocking()
    {
        ChangePlanScanSource scan = new(_link, _now.AddDays(-8),
            [new("SYN-APP-01", "Success"), new("SYN-WEB-02", "Partial"), new("SYN-DOWN-03", "Unreachable")],
            [
                new("SYN-APP-01", "WindowsService", "SynService", @"SYN\svc_old"),
                new("SYN-APP-01", "IisVirtualDirectory", "Default/syn", @"SYN\svc_old"),
                new("SYN-WEB-02", "IisAppPool", "SynPool", @"SYN\svc_old")
            ]);

        ChangePlanPreviewRow[] rows = [.. ChangePlanRules.BuildPreview([new ChangePlanPreviewAccount(_a, "gmsaSyn1", scan)], _now)];

        rows.Select(r => (r.ServerName, r.ComponentType, r.Flag)).Should().Equal(
            ("SYN-APP-01", "IisVirtualDirectory", ChangePlanFlag.ManualOnly),
            ("SYN-APP-01", "WindowsService", ChangePlanFlag.StaleScan),
            ("SYN-DOWN-03", null, ChangePlanFlag.NotCovered),
            ("SYN-WEB-02", "IisAppPool", ChangePlanFlag.NotCovered));
        rows.Should().OnlyContain(r => r.ScanLinkId == _link && r.TargetIdentity == "gmsaSyn1" && r.ScanAt == scan.ScanAt);
    }

    [Fact]
    public void Preview_FreshScan_IsOk_AndSevenDaysIsTheBoundary()
    {
        ChangePlanScanSource fresh = new(_link, _now.AddDays(-ChangePlanRules.ScanFreshDays), [new("SYN-APP-01", "Success")],
            [new("SYN-APP-01", "ScheduledTask", @"\SynTask", @"SYN\svc_old")]);

        ChangePlanRules.BuildPreview([new ChangePlanPreviewAccount(_a, "gmsaSyn1", fresh)], _now).Single().Flag.Should().Be(ChangePlanFlag.Ok);
        ChangePlanRules.BuildPreview([new ChangePlanPreviewAccount(_a, "gmsaSyn1", fresh with { ScanAt = fresh.ScanAt.AddTicks(-1) })], _now)
            .Single().Flag.Should().Be(ChangePlanFlag.StaleScan);
    }

    [Fact]
    public void Preview_AnsweredServerWithNothingFound_HasNoRow_BecauseNotFoundIsOnlyAbsentFromTheScan()
    {
        ChangePlanScanSource scan = new(_link, _now, [new("SYN-APP-01", "Success"), new("SYN-APP-02", "Success")],
            [new("SYN-APP-01", "WindowsService", "SynService", @"SYN\svc_old")]);

        ChangePlanRules.BuildPreview([new ChangePlanPreviewAccount(_a, "gmsaSyn1", scan)], _now).Select(r => r.ServerName).Should().Equal("SYN-APP-01");
    }

    [Fact]
    public void Preview_ScanThatFoundNothing_KeepsTheAccountVisible_AsNothingFound_NotAsUnused()
    {
        ChangePlanScanSource scan = new(_link, _now, [new("SYN-APP-01", "Success")], []);

        ChangePlanPreviewRow row = ChangePlanRules.BuildPreview([new ChangePlanPreviewAccount(_a, "gmsaSyn1", scan)], _now).Single();

        row.Should().Be(new ChangePlanPreviewRow(_a, _link, null, null, null, null, "gmsaSyn1", ChangePlanFlag.NothingFound, _now));
        ChangePlanRules.FlagLabel(ChangePlanFlag.NothingFound).Should().Contain("kullanılmıyor anlamına gelmez");
    }

    [Theory]
    [InlineData("planner", "approverIsPlanner")]
    [InlineData("previewer", "approverChangedPlan")]
    [InlineData("editor", "approverChangedPlan")]
    [InlineData("other", null)]
    public void ApproverSeparation_RefusesPlannerPreviewerAndEditors(string who, string? expected)
    {
        Guid planner = Guid.NewGuid(), previewer = Guid.NewGuid(), editor = Guid.NewGuid();
        Guid approver = who switch { "planner" => planner, "previewer" => previewer, "editor" => editor, _ => Guid.NewGuid() };

        ChangePlanRules.ApproverRefusal(approver, planner, previewer, [editor]).Should().Be(expected);
    }

    [Theory]
    [InlineData("OCO-12345", true)]
    [InlineData(" oco-77 ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void OcoNumber_IsCheckedForFormatOnly(string value, bool valid)
    {
        (ChangePlanRules.OcoNumber(value) is not null).Should().Be(valid);
        ChangePlanRules.OcoNumber(new string('9', 65)).Should().BeNull();
    }

    [Theory]
    [InlineData("gmsaSyn1", "gmsaSyn1")]
    [InlineData(@" SYN\gmsaSyn14chars$ ", @"SYN\gmsaSyn14chars$")]
    [InlineData("gmsaSynNameTooLong16", null)]
    [InlineData("   ", null)]
    [InlineData(@"SYN\$", null)]
    public void TargetName_FollowsThe031Rule(string value, string? expected) => ChangePlanRules.TargetName(value).Should().Be(expected);

    [Fact]
    public void OpenStatuses_AreAllButCompletedAndCancelled()
    {
        Enum.GetValues<ChangePlanStatus>().Where(ChangePlanRules.IsOpen).Should().Equal(
            ChangePlanStatus.Draft, ChangePlanStatus.Previewed, ChangePlanStatus.Approved, ChangePlanStatus.InProgress);
    }
}
