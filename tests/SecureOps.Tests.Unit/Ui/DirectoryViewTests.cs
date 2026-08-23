using FluentAssertions;
using SecureOps.Shared.Contracts.Directory;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins the Directory Explorer presentation rules.
/// </summary>
/// <remarks>
/// The important ones are not the labels. They are that a bounded traversal is never reported as a
/// negative membership answer, and that an unread directory attribute is never rendered as a value.
/// Both are the kind of mistake that turns an evidence screen into a wrong authorization decision.
/// </remarks>
public sealed class DirectoryViewTests
{
    [Theory]
    [InlineData("Security")]
    [InlineData("Distribution")]
    public void CategoryLabel_TranslatesTheContractVocabulary(string category) =>
        DirectoryView.CategoryLabel(category).Should().NotBe(category);

    [Theory]
    [InlineData("Universal")]
    [InlineData("DomainLocal")]
    public void ScopeLabel_TranslatesTheContractVocabulary(string scope) =>
        DirectoryView.ScopeLabel(scope).Should().NotBe(scope);

    [Theory]
    [InlineData("User")]
    [InlineData("Group")]
    [InlineData("Computer")]
    [InlineData("Other")]
    public void MemberTypeLabel_TranslatesEveryMemberType(string memberType) =>
        DirectoryView.MemberTypeLabel(memberType).Should().NotBe(memberType);

    [Theory]
    [InlineData("Security")]
    [InlineData("Global")]
    [InlineData("User")]
    public void Labels_LeaveAnUnknownValueAsItIs(string _)
    {
        // A vocabulary added server-side must surface as itself rather than be silently mapped onto
        // an existing meaning.
        DirectoryView.CategoryLabel("SomethingNew").Should().Be("SomethingNew");
        DirectoryView.ScopeLabel("SomethingNew").Should().Be("SomethingNew");
        DirectoryView.MemberTypeLabel("SomethingNew").Should().Be("SomethingNew");
    }

    [Theory]
    [InlineData("GroupManagedServiceAccount")]
    [InlineData("ManagedServiceAccount")]
    [InlineData("User")]
    public void AccountTypeEvidenceLabel_DescribesTheObjectWithoutClassifyingTheAccount(string evidence)
    {
        // The API reports what the directory object is. Nothing here may assert that an account is
        // or is not a service account — that judgement is the operator's.
        string label = DirectoryView.AccountTypeEvidenceLabel(evidence);

        label.Should().NotBeNullOrWhiteSpace();
        label.Should().NotContain("kesin");
    }

    [Fact]
    public void GroupName_FallsThroughEveryNullableNamingField()
    {
        DirectoryView.GroupName(Group(name: "Ops", sam: "GRP-OPS")).Should().Be("Ops");
        DirectoryView.GroupName(Group(name: null, sam: "GRP-OPS")).Should().Be("GRP-OPS");
        DirectoryView.GroupName(Group(name: null, sam: null, dn: "CN=Ops,DC=x")).Should().Be("CN=Ops,DC=x");
        DirectoryView.GroupName(Group(name: null, sam: null)).Should().Be(DirectoryView.UnknownText);
    }

    [Fact]
    public void GroupName_TreatsWhitespaceAsAbsent()
    {
        DirectoryView.GroupName(Group(name: "   ", sam: "GRP-OPS")).Should().Be("GRP-OPS");
    }

    [Fact]
    public void Flag_RendersNullAsUnknownRatherThanFalse()
    {
        // A directory that did not return an attribute has not said the attribute is false.
        DirectoryView.Flag(null, "Evet", "Hayır").Should().Be(DirectoryView.UnknownText);
        DirectoryView.Flag(true, "Evet", "Hayır").Should().Be("Evet");
        DirectoryView.Flag(false, "Evet", "Hayır").Should().Be("Hayır");
    }

    [Fact]
    public void Timestamp_AndDays_RenderNullAsUnknown()
    {
        DirectoryView.Timestamp(null).Should().Be(DirectoryView.UnknownText);
        DirectoryView.Days(null).Should().Be(DirectoryView.UnknownText);
        DirectoryView.Days(0).Should().Be("0 gün");
    }

    [Fact]
    public void TraversalLimits_IsEmptyForACompleteWalk()
    {
        DirectoryView.TraversalLimits(Traversal()).Should().BeEmpty();
    }

    [Fact]
    public void TraversalLimits_ReportsEachBoundSeparately()
    {
        // Listed rather than summarised: a depth limit and a provider result limit call for
        // different follow-up, and a detected cycle is a directory finding in its own right.
        IReadOnlyList<string> limits = DirectoryView.TraversalLimits(Traversal(
            depth: true, node: true, edge: true, provider: true, cycle: true));

        limits.Should().HaveCount(5);
        limits.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void NegativeIsConclusive_IsTrueOnlyForAnUnboundedCompleteWalk()
    {
        DirectoryView.NegativeIsConclusive(Paths(isMember: false)).Should().BeTrue();
    }

    [Fact]
    public void NegativeIsConclusive_IsFalseWhenTheServerReportsTruncation()
    {
        // The guard that stops a bounded search being presented as an authorization answer.
        DirectoryView.NegativeIsConclusive(
            Paths(isMember: false, traversal: Traversal(truncated: true))).Should().BeFalse();
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void NegativeIsConclusive_IsFalseWhenAnyLimitWasReached(
        bool depth, bool node, bool edge, bool provider)
    {
        DirectoryView.NegativeIsConclusive(Paths(
            isMember: false,
            traversal: Traversal(depth: depth, node: node, edge: edge, provider: provider)))
            .Should().BeFalse();
    }

    [Fact]
    public void NegativeIsConclusive_IsFalseWhenPathsWereTruncated()
    {
        DirectoryView.NegativeIsConclusive(Paths(isMember: false, pathsTruncated: true))
            .Should().BeFalse();
    }

    [Fact]
    public void NegativeIsConclusive_IsFalseForAPositiveResult()
    {
        // Only a negative answer needs the guard; a proven membership is never "conclusively not".
        DirectoryView.NegativeIsConclusive(Paths(isMember: true)).Should().BeFalse();
    }

    [Fact]
    public void HasPrivilegedMembership_IsTrueForEitherDirectOrTransitive()
    {
        DirectoryView.HasPrivilegedMembership(Privileged(direct: false, transitive: false))
            .Should().BeFalse();
        DirectoryView.HasPrivilegedMembership(Privileged(direct: true, transitive: false))
            .Should().BeTrue();
        DirectoryView.HasPrivilegedMembership(Privileged(direct: false, transitive: true))
            .Should().BeTrue();
    }

    private static DirectoryGroupSummaryDto Group(
        string? name = "Ops",
        string? sam = "GRP-OPS",
        string? dn = null) =>
        new(null, name, sam, dn, null, "Security", "Global");

    private static DirectoryTraversalMetadataDto Traversal(
        bool depth = false,
        bool node = false,
        bool edge = false,
        bool provider = false,
        bool cycle = false,
        bool truncated = false) =>
        new(10, 20, 3, cycle, depth, node, edge, provider, truncated);

    private static DirectoryMembershipPathResponse Paths(
        bool isMember,
        bool pathsTruncated = false,
        DirectoryTraversalMetadataDto? traversal = null) =>
        new(isMember, false, [], pathsTruncated, traversal ?? Traversal());

    private static DirectoryPrivilegedMembershipResponse Privileged(bool direct, bool transitive) =>
        new(
            [new DirectoryPrivilegedMembershipDto("DOM\\Admins", true, Group(), direct, transitive, [], false)],
            Traversal());
}
