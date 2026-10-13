using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Tests.Unit.ServiceAccounts;

/// <summary>
/// Scan comparison rules (G-33, synthetic data): "not found" follows only a fully scanned server and never means "not used";
/// what the newer scan did not cover is unknown, not gone; an unchanged result is not a usage verdict.
/// </summary>
public sealed class UsageScanDiffTests
{
    [Fact]
    public void FoundThenNotFound_IsNeverNotUsed_AndLostInformationIsNotGone()
    {
        ScanDiffResult diff = UsageScanDiff.Compute(
            [Srv("SYN-APP01", ScanServerResult.Success, 1), Srv("SYN-APP02", ScanServerResult.Success, 1), Srv("SYN-APP03", ScanServerResult.Unreachable)],
            [Item("SYN-APP01", "SynPool"), Item("SYN-APP02", "SynTask")],
            [Srv("SYN-APP01", ScanServerResult.Success), Srv("SYN-APP02", ScanServerResult.Partial), Srv("SYN-APP03", ScanServerResult.Success, 1)],
            [Item("SYN-APP03", "SynSvc")]);

        diff.Servers.Single(s => s.ServerName == "SYN-APP01").Should().Match<ScanServerDiff>(s =>
            s.Change == ScanServerChange.NoLongerFound && s.Text.Contains("kullanılmadığını göstermez"));
        diff.Servers.Single(s => s.ServerName == "SYN-APP02").Change.Should().Be(ScanServerChange.InformationLost);
        diff.Servers.Single(s => s.ServerName == "SYN-APP03").Change.Should().Be(ScanServerChange.InformationArrived);
        diff.Components.Single(c => c.ServerName == "SYN-APP01").Change.Should().Be(ScanComponentChange.NotFoundNow);
        diff.Components.Single(c => c.ServerName == "SYN-APP02").Change.Should().Be(ScanComponentChange.UnknownNow, "a partial scan may have missed it");
        diff.Components.Single(c => c.ServerName == "SYN-APP03").Should().Match<ScanComponentDiff>(c =>
            c.Change == ScanComponentChange.Added && c.Text.Contains("yeni olmayabilir"));
    }

    [Fact]
    public void PartialServerWithOtherMatches_DoesNotTurnAMissingComponentIntoNotFound()
    {
        // The server outcome is Found (another match), but a source failed: the missing component is unknown.
        ScanDiffResult diff = UsageScanDiff.Compute(
            [Srv("SYN-APP01", ScanServerResult.Success, 2)], [Item("SYN-APP01", "SynPool"), Item("SYN-APP01", "SynTask")],
            [Srv("SYN-APP01", ScanServerResult.Partial, 1)], [Item("SYN-APP01", "SynPool")]);

        diff.Servers.Single().Change.Should().Be(ScanServerChange.Unchanged);
        diff.Components.Should().ContainSingle().Which.Change.Should().Be(ScanComponentChange.UnknownNow);
    }

    [Fact]
    public void PlanChanges_AreLabelledByPlanNotByUse()
    {
        ScanDiffResult diff = UsageScanDiff.Compute(
            [Srv("SYN-APP01", ScanServerResult.Success, 1), Srv("SYN-APP02", ScanServerResult.Success)], [Item("SYN-APP01", "SynPool")],
            [Srv("syn-app01", ScanServerResult.Success, 1), Srv("SYN-APP09", ScanServerResult.Success)], [Item("syn-app01", "synpool")]);

        diff.Servers.Single(s => s.ServerName == "SYN-APP02").Change.Should().Be(ScanServerChange.NotPlannedNow);
        diff.Servers.Single(s => s.ServerName == "SYN-APP09").Change.Should().Be(ScanServerChange.NewlyPlanned);
        diff.Servers.Single(s => s.ServerName == "syn-app01").Change.Should().Be(ScanServerChange.Unchanged, "names compare case-insensitively");
        diff.Components.Should().BeEmpty();
    }

    [Fact]
    public void IdentityChange_IsReported_AndOrderIsByChangeThenServer()
    {
        ScanDiffResult diff = UsageScanDiff.Compute(
            [Srv("SYN-APP01", ScanServerResult.Success, 2)], [Item("SYN-APP01", "SynPool", "SYN\\svc_old"), Item("SYN-APP01", "SynGone")],
            [Srv("SYN-APP01", ScanServerResult.Success, 2)], [Item("SYN-APP01", "SynPool", "SYN\\svc_new"), Item("SYN-APP01", "SynNew")]);

        diff.Components.Select(c => c.Change).Should().Equal(ScanComponentChange.Added, ScanComponentChange.NotFoundNow, ScanComponentChange.IdentityChanged);
        diff.Components[^1].Should().Match<ScanComponentDiff>(c => c.PreviousIdentity == "SYN\\svc_old" && c.CurrentIdentity == "SYN\\svc_new");
    }

    [Fact]
    public void SameScan_HasNoChange()
    {
        ScanDiffServer[] servers = [Srv("SYN-APP01", ScanServerResult.Success, 1), Srv("SYN-APP02", ScanServerResult.Unreachable)];
        ScanDiffItem[] items = [Item("SYN-APP01", "SynPool")];

        ScanDiffResult diff = UsageScanDiff.Compute(servers, items, servers, items);

        diff.Components.Should().BeEmpty();
        diff.Servers.Should().OnlyContain(s => s.Change == ScanServerChange.Unchanged);
        diff.Servers.Single(s => s.ServerName == "SYN-APP02").Text.Should().StartWith("Hâlâ bilgi yok");
    }

    private static ScanDiffServer Srv(string name, ScanServerResult result, int matches = 0) => new(name, result, matches);

    private static ScanDiffItem Item(string server, string component, string identity = "SYN\\svc_synapp") => new(server, "IisAppPool", component, identity);
}
