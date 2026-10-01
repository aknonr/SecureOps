using FluentAssertions;
using SecureOps.Shared.Contracts.Directory;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins the Active Directory presentation rules.
/// </summary>
/// <remarks>
/// The important ones are not the labels. They are that a bounded traversal is never reported as a
/// negative membership answer, that an unread directory attribute is never rendered as a value, and
/// that nothing here classifies an account as human, PAM, or a service. Each of those mistakes turns
/// an evidence screen into a wrong operational decision.
/// <para>
/// Every fixture is synthetic. No real account, group, server, or PAM value appears in this file.
/// </para>
/// </remarks>
public sealed class DirectoryViewTests
{
    // ---- vocabulary --------------------------------------------------------------------------

    [Theory]
    [InlineData("Security", "Güvenlik Grubu")]
    [InlineData("Distribution", "Dağıtım Grubu")]
    public void CategoryLabel_NamesTheObjectRatherThanAnAdjective(string category, string expected) =>
        DirectoryView.CategoryLabel(category).Should().Be(expected);

    [Theory]
    [InlineData("Security")]
    [InlineData("Distribution")]
    public void CategoryExplanation_ExistsForEveryKnownCategory(string category) =>
        DirectoryView.CategoryExplanation(category).Should().NotBeNullOrWhiteSpace();

    [Theory]
    [InlineData("Global", "Global")]
    [InlineData("Universal", "Universal")]
    [InlineData("DomainLocal", "Domain Local")]
    public void ScopeLabel_ShowsTheDirectoryEnumFaithfully(string scope, string expected)
    {
        // Deliberately untranslated: these are the names in every Microsoft tool an administrator
        // will cross-reference. The meaning belongs in the help text, not in a renamed enum.
        DirectoryView.ScopeLabel(scope).Should().Be(expected);
    }

    [Theory]
    [InlineData("Global")]
    [InlineData("Universal")]
    [InlineData("DomainLocal")]
    public void ScopeExplanation_ExistsForEveryKnownScope(string scope) =>
        DirectoryView.ScopeExplanation(scope).Should().NotBeNullOrWhiteSpace();

    [Theory]
    [InlineData("User", "Kullanıcı")]
    [InlineData("Group", "AD Grubu")]
    [InlineData("Computer", "Bilgisayar")]
    [InlineData("Other", "Diğer")]
    public void MemberTypeLabel_DescribesTheObjectType(string memberType, string expected) =>
        DirectoryView.MemberTypeLabel(memberType).Should().Be(expected);

    [Fact]
    public void Labels_LeaveAnUnknownValueAsItIs()
    {
        // A vocabulary added server-side must surface as itself rather than be silently mapped onto
        // an existing meaning.
        DirectoryView.CategoryLabel("SomethingNew").Should().Be("SomethingNew");
        DirectoryView.ScopeLabel("SomethingNew").Should().Be("SomethingNew");
        DirectoryView.MemberTypeLabel("SomethingNew").Should().Be("SomethingNew");
        DirectoryView.CategoryExplanation("SomethingNew").Should().BeNull();
        DirectoryView.ScopeExplanation("SomethingNew").Should().BeNull();
    }

    [Theory]
    [InlineData("Direct", "Doğrudan Üyelik")]
    [InlineData("Primary", "Birincil Grup")]
    [InlineData("Transitive", "Dolaylı / İç İçe Üyelik")]
    public void MembershipKindLabel_KeepsTheThreeRelationshipsApart(string kind, string expected)
    {
        // They are removed in three different places, which is the whole reason the contract keeps
        // them separate.
        DirectoryView.MembershipKindLabel(kind).Should().Be(expected);
        DirectoryView.MembershipKindExplanation(kind).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void MembershipKindLabel_FallsBackWhenTheServerStatesNoKind()
    {
        DirectoryView.MembershipKindLabel(null).Should().Be("Üyelik");
        DirectoryView.MembershipKindExplanation(null).Should().BeNull();
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
        label.Should().NotContain("PAM");
    }

    // ---- names -------------------------------------------------------------------------------

    [Fact]
    public void GroupName_FallsThroughEveryNullableNamingField()
    {
        DirectoryView.GroupName(Group(name: "Ops", sam: "GRP-OPS")).Should().Be("Ops");
        DirectoryView.GroupName(Group(name: null, sam: "GRP-OPS")).Should().Be("GRP-OPS");
        DirectoryView.GroupName(Group(name: null, sam: null, dn: "CN=Ops,DC=x")).Should().Be("CN=Ops,DC=x");
        DirectoryView.GroupName(Group(name: null, sam: null)).Should().Be(DirectoryView.UnknownText);
    }

    [Fact]
    public void GroupName_TreatsWhitespaceAsAbsent() =>
        DirectoryView.GroupName(Group(name: "   ", sam: "GRP-OPS")).Should().Be("GRP-OPS");

    [Fact]
    public void ShowAccountNameSeparately_IsFalseWhenTheValuesAreTheSame()
    {
        // In many directories a group's name and sAMAccountName are identical. Printing the same
        // string twice under two labels wastes a column and implies a distinction that is not there.
        DirectoryView.ShowAccountNameSeparately("GRP-OPS", "GRP-OPS").Should().BeFalse();
        DirectoryView.ShowAccountNameSeparately("grp-ops", "GRP-OPS").Should().BeFalse();
        DirectoryView.ShowAccountNameSeparately(" GRP-OPS ", "GRP-OPS").Should().BeFalse();
    }

    [Fact]
    public void ShowAccountNameSeparately_IsTrueOnlyWhenBothExistAndDiffer()
    {
        DirectoryView.ShowAccountNameSeparately("Ops Team", "GRP-OPS").Should().BeTrue();
        DirectoryView.ShowAccountNameSeparately(null, "GRP-OPS").Should().BeFalse();
        DirectoryView.ShowAccountNameSeparately("Ops Team", null).Should().BeFalse();
    }

    [Fact]
    public void ExactGroupIdentifier_PrefersTheAccountNameAndNeverTheDistinguishedName()
    {
        // sAMAccountName resolves most reliably server-side, and the endpoints reject raw DNs — so a
        // DN must never be what a follow-up lookup sends.
        DirectoryView.ExactGroupIdentifier(Group(name: "Ops", sam: "GRP-OPS")).Should().Be("GRP-OPS");
        DirectoryView.ExactGroupIdentifier(Group(name: "Ops", sam: null)).Should().Be("Ops");
        DirectoryView.ExactGroupIdentifier(Group(name: null, sam: null, dn: "CN=Ops,DC=x")).Should().BeNull();
    }

    // ---- values ------------------------------------------------------------------------------

    [Fact]
    public void Flag_RendersNullAsUnknownRatherThanFalse()
    {
        // A directory that did not return an attribute has not said the attribute is false.
        DirectoryView.Flag(null, "Evet", "Hayır").Should().Be(DirectoryView.UnknownText);
        DirectoryView.Flag(true, "Evet", "Hayır").Should().Be("Evet");
        DirectoryView.Flag(false, "Evet", "Hayır").Should().Be("Hayır");
    }

    [Fact]
    public void Timestamp_DaysAndCount_RenderNullAsUnknown()
    {
        DirectoryView.Timestamp(null).Should().Be(DirectoryView.UnknownText);
        DirectoryView.Days(null).Should().Be(DirectoryView.UnknownText);
        DirectoryView.Count(null).Should().Be(DirectoryView.UnknownText);
        DirectoryView.Days(0).Should().Be("0 gün");
        DirectoryView.Count(0).Should().Be("0");
    }

    [Fact]
    public void UnknownAndNotDefined_AreDifferentStatements()
    {
        // "We could not read it" and "the directory says nothing is configured" are different facts
        // and must not collapse into one phrase.
        DirectoryView.UnknownText.Should().NotBe(DirectoryView.NotDefinedText);
    }

    // ---- traversal ---------------------------------------------------------------------------

    [Fact]
    public void TraversalLimits_IsEmptyForACompleteWalk() =>
        DirectoryView.TraversalLimits(Traversal()).Should().BeEmpty();

    [Fact]
    public void TraversalLimits_IsEmptyWhenEvidenceWasNotCollectedAtAll()
    {
        // Traversal became nullable when principal evidence and membership evidence were separated.
        // A null means no walk was attempted, which is not the same as a bounded one.
        DirectoryView.TraversalLimits(null).Should().BeEmpty();
    }

    [Fact]
    public void TraversalLimits_ReportsEachBoundSeparately()
    {
        // Listed rather than summarised: a depth limit and a provider result limit call for
        // different follow-up, and a detected cycle is a directory finding in its own right.
        IReadOnlyList<string> limits = DirectoryView.TraversalLimits(Traversal(
            depth: true, node: true, edge: true, provider: true, cycle: true));

        limits.Should().HaveCount(5).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void NegativeIsConclusive_IsTrueOnlyForAnUnboundedCompleteWalk() =>
        DirectoryView.NegativeIsConclusive(Paths(isMember: false)).Should().BeTrue();

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
        bool depth, bool node, bool edge, bool provider) =>
        DirectoryView.NegativeIsConclusive(Paths(
            isMember: false,
            traversal: Traversal(depth: depth, node: node, edge: edge, provider: provider)))
            .Should().BeFalse();

    [Fact]
    public void NegativeIsConclusive_IsFalseWhenPathsWereTruncated() =>
        DirectoryView.NegativeIsConclusive(Paths(isMember: false, pathsTruncated: true))
            .Should().BeFalse();

    [Fact]
    public void NegativeIsConclusive_IsFalseForAPositiveResult() =>
        DirectoryView.NegativeIsConclusive(Paths(isMember: true)).Should().BeFalse();

    [Fact]
    public void PathMembershipKind_ReadsTheKindFromTheProvenChain()
    {
        // Primary membership matters here: it is not an explicit link and cannot be removed the way
        // a direct one can, so the verdict has to be able to say so.
        DirectoryMembershipPathResponse response = Paths(
            isMember: true,
            paths: [new DirectoryMembershipPathDto([Group(name: "Domain Users", sam: "domain-users", kind: "Primary")])]);

        DirectoryView.PathMembershipKind(response).Should().Be("Primary");
    }

    [Fact]
    public void PathMembershipKind_IsNullWhenTheServerNamesNoKind() =>
        DirectoryView.PathMembershipKind(
            Paths(isMember: true, paths: [new DirectoryMembershipPathDto([Group()])]))
            .Should().BeNull();

    // ---- analysis ----------------------------------------------------------------------------

    [Fact]
    public void AnalysisIsComplete_RequiresBothTheServerFlagAndAnUnboundedWalk()
    {
        DirectoryView.AnalysisIsComplete(Analysis(isComplete: true)).Should().BeTrue();
        DirectoryView.AnalysisIsComplete(Analysis(isComplete: false)).Should().BeFalse();
        DirectoryView.AnalysisIsComplete(Analysis(isComplete: true, traversal: Traversal(truncated: true)))
            .Should().BeFalse();
        DirectoryView.AnalysisIsComplete(Analysis(isComplete: true, traversal: Traversal(depth: true)))
            .Should().BeFalse();
    }

    [Fact]
    public void HasPrivilegedMembership_IsTrueForEitherDirectOrTransitive()
    {
        DirectoryView.HasPrivilegedMembership(Privileged(direct: false, transitive: false)).Should().BeFalse();
        DirectoryView.HasPrivilegedMembership(Privileged(direct: true, transitive: false)).Should().BeTrue();
        DirectoryView.HasPrivilegedMembership(Privileged(direct: false, transitive: true)).Should().BeTrue();
    }

    // ---- synthetic fixtures ------------------------------------------------------------------

    private static DirectoryGroupSummaryDto Group(
        string? name = "Ornek Operasyon",
        string? sam = "GRP-ORNEK-OPERASYON",
        string? dn = null,
        string? kind = null) =>
        new(null, name, sam, dn, null, "Security", "Global", kind);

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
        DirectoryTraversalMetadataDto? traversal = null,
        IReadOnlyList<DirectoryMembershipPathDto>? paths = null) =>
        new(isMember, false, paths ?? [], pathsTruncated, traversal ?? Traversal());

    private static DirectoryGroupAnalysisResponse Analysis(
        bool isComplete,
        DirectoryTraversalMetadataDto? traversal = null) =>
        new(
            new DirectoryGroupDetailDto(null, "Ornek", "GRP-ORNEK", null, null, "Security", "Global", null, 0),
            [], [], [], [], [],
            new DirectoryGroupParentMembershipsDto([], [], Traversal()),
            traversal ?? Traversal(),
            isComplete);

    private static DirectoryPrivilegedMembershipResponse Privileged(bool direct, bool transitive) =>
        new(
            [new DirectoryPrivilegedMembershipDto("ORNEK\\Yoneticiler", true, Group(), direct, transitive, [], false)],
            Traversal());
}
