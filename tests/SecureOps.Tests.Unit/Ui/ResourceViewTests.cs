using FluentAssertions;
using SecureOps.Domain.Resources;
using SecureOps.Shared.Contracts.Resources;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins the catalogue and shift-set presentation rules.
/// </summary>
/// <remarks>
/// The properties held here are the ones that would quietly produce a wrong screen rather than an
/// obvious crash: opening order surviving a save, duplicates being surfaced rather than collapsed,
/// and a partially resolvable set never being described in terms the contract does not supply.
/// </remarks>
public sealed class ResourceViewTests
{
    [Fact]
    public void LinkIds_PreserveOpeningOrder()
    {
        // Array order is the opening order in the contract, so sorting here would silently reorder
        // somebody's shift start.
        ShiftSetResponse set = Set("Sabah", Link("C"), Link("A"), Link("B"));

        ResourceView.LinkIds(set).Should().Equal(set.Links.Select(link => link.Id));
    }

    [Fact]
    public void Move_SwapsAdjacentEntries()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();

        ResourceView.Move([a, b, c], 0, 1).Should().Equal(b, a, c);
        ResourceView.Move([a, b, c], 2, -1).Should().Equal(a, c, b);
    }

    [Fact]
    public void Move_LeavesTheListUnchangedAtTheBounds()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();

        ResourceView.Move([a, b], 0, -1).Should().Equal(a, b);
        ResourceView.Move([a, b], 1, 1).Should().Equal(a, b);
    }

    [Fact]
    public void ValidateSet_RejectsAnEmptyName()
    {
        ResourceView.ValidateSet("   ", [], [], null).Should().NotBeNull();
    }

    [Fact]
    public void ValidateSet_RejectsADuplicateNameIgnoringCase()
    {
        // Per-owner uniqueness is ordinal case-insensitive in the contract.
        ShiftSetResponse existing = Set("Sabah");

        ResourceView.ValidateSet("SABAH", [], [existing], null).Should().NotBeNull();
    }

    [Fact]
    public void ValidateSet_AllowsASetToKeepItsOwnNameWhileEditing()
    {
        ShiftSetResponse existing = Set("Sabah");

        ResourceView.ValidateSet("Sabah", [], [existing], existing.Id).Should().BeNull();
    }

    [Fact]
    public void ValidateSet_RejectsDuplicateLinksRatherThanCollapsingThem()
    {
        // The server rejects duplicates on purpose; collapsing them here would hide an editing
        // mistake instead of reporting it.
        var link = Guid.NewGuid();

        ResourceView.ValidateSet("Sabah", [link, link], [], null).Should().NotBeNull();
    }

    [Fact]
    public void ValidateSet_RejectsMoreThanTheDocumentedLinkCeiling()
    {
        Guid[] tooMany = Enumerable.Range(0, ResourceView.MaxLinksPerSet + 1)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        ResourceView.ValidateSet("Sabah", tooMany, [], null).Should().NotBeNull();
    }

    [Fact]
    public void CanCreateSet_StopsAtTheDocumentedCeiling()
    {
        ShiftSetResponse[] full = Enumerable.Range(0, ResourceView.MaxSets)
            .Select(index => Set($"Set {index}"))
            .ToArray();

        ResourceView.CanCreateSet(Preferences(full)).Should().BeFalse();
        ResourceView.CanCreateSet(Preferences([Set("Tek")])).Should().BeTrue();
    }

    [Fact]
    public void PartialSetNotice_ClaimsNothingTheContractDoesNotSupply()
    {
        // Resolution returns only openable links and no count or reason for what was excluded, so
        // the notice must not state a number or attribute a cause — either would be invented, and
        // naming permission would leak that a restricted entry exists.
        ResourceView.PartialSetNotice.Should().NotContain("adet");
        ResourceView.PartialSetNotice.Should().NotContain("yetkiniz");
        ResourceView.PartialSetNotice.Should().Contain("kullanılamıyor");
    }

    [Fact]
    public void IsFavourite_IsFalseBeforePreferencesLoad()
    {
        ResourceView.IsFavourite(null, Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void Placement_OmitsMissingValuesAndNeverInventsOne()
    {
        ResourceView.Placement(Link("A") with { Environment = "Test", Location = "Lab" })
            .Should().Be("Test · Lab");
        ResourceView.Placement(Link("A") with { Environment = "Test", Location = null })
            .Should().Be("Test");
        ResourceView.Placement(Link("A") with { Environment = null, Location = null })
            .Should().BeNull();
    }

    [Fact]
    public void Host_DegradesToNullRatherThanThrowingInsideAListRender()
    {
        ResourceView.Host(Link("A") with { Url = "https://example.invalid/dashboard" })
            .Should().Be("example.invalid");
        ResourceView.Host(Link("A") with { Url = "not a url" }).Should().BeNull();
    }

    private static ResourceLink Link(string name) => new(
        Guid.NewGuid(), Guid.NewGuid(), name, "https://example.invalid/x", "Amaç",
        null, null, null, [], 0, true, false, 1, DateTimeOffset.UnixEpoch);

    private static ShiftSetResponse Set(string name, params ResourceLink[] links) =>
        new(Guid.NewGuid(), name, links, false);

    private static ResourcePreferencesResponse Preferences(IReadOnlyList<ShiftSetResponse> sets) =>
        new(1, [], sets, null);
}
