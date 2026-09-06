using SecureOps.Infrastructure.Access;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Covers the client-side mirror of the server's access-decision validation.
/// </summary>
/// <remarks>
/// These rules carry more weight than ordinary form validation. The API answers a blank reason, an
/// empty role set, an unknown role, and "another administrator already decided this" with the same
/// <c>AccessRequestInvalidState</c> code. Catching the input cases here is what lets the UI treat a
/// 409 that still arrives as a genuine conflict. If these rules drift from the server's, that
/// inference breaks — so the boundaries are pinned here deliberately.
/// </remarks>
public sealed class AccessDecisionRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void ValidateReason_RejectsBlank(string? reason)
    {
        Assert.NotNull(AccessDecisionRules.ValidateReason(reason));
    }

    [Fact]
    public void ValidateReason_AcceptsOrdinaryJustification()
    {
        Assert.Null(AccessDecisionRules.ValidateReason("Vardiya devri için onaylandı."));
    }

    [Fact]
    public void ValidateReason_AcceptsExactlyMaxLength()
    {
        string reason = new('a', AccessDecisionRules.MaxReasonLength);

        Assert.Null(AccessDecisionRules.ValidateReason(reason));
    }

    [Fact]
    public void ValidateReason_RejectsOverMaxLength()
    {
        string reason = new('a', AccessDecisionRules.MaxReasonLength + 1);

        Assert.NotNull(AccessDecisionRules.ValidateReason(reason));
    }

    [Fact]
    public void ValidateReason_MeasuresTrimmedLength()
    {
        // The server trims before measuring, so padding must not be counted against the limit;
        // being stricter here would reject a reason the API would have accepted.
        string reason = "   " + new string('a', AccessDecisionRules.MaxReasonLength) + "   ";

        Assert.Null(AccessDecisionRules.ValidateReason(reason));
    }

    [Fact]
    public void ValidateRoles_RejectsNull()
    {
        Assert.NotNull(AccessDecisionRules.ValidateRoles(null));
    }

    [Fact]
    public void ValidateRoles_RejectsEmpty()
    {
        Assert.NotNull(AccessDecisionRules.ValidateRoles([]));
    }

    [Fact]
    public void ValidateRoles_RejectsUnknownRole()
    {
        string? message = AccessDecisionRules.ValidateRoles(["Operator", "NotARole"]);

        Assert.NotNull(message);
        Assert.Contains("NotARole", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRoles_AcceptsSingleKnownRole()
    {
        Assert.Null(AccessDecisionRules.ValidateRoles(["Operator"]));
    }

    [Fact]
    public void ValidateRoles_AcceptsMultipleRoles()
    {
        // The contract models roles as a set, and the server accepts up to sixteen, so multiple
        // assignment is supported behaviour rather than something the UI invented.
        Assert.Null(AccessDecisionRules.ValidateRoles(["Lead", "JiraPublisher"]));
    }

    [Fact]
    public void ValidateRoles_RejectsMoreThanMax()
    {
        string[] roles = Enumerable
            .Range(0, AccessDecisionRules.MaxRoles + 1)
            .Select(_ => "Operator")
            .ToArray();

        Assert.NotNull(AccessDecisionRules.ValidateRoles(roles));
    }

    [Fact]
    public void AssignableRoles_MatchesServerCatalog()
    {
        // Read from the catalog rather than duplicated, so a role added or retired on the server
        // cannot leave the picker offering something the API would reject.
        Assert.Equal(
            AccessRoleCatalog.RoleCodes.OrderBy(role => role, StringComparer.Ordinal),
            AccessDecisionRules.AssignableRoles);
    }

    [Fact]
    public void AssignableRoles_AreAllAcceptedByValidation()
    {
        foreach (string role in AccessDecisionRules.AssignableRoles)
        {
            Assert.Null(AccessDecisionRules.ValidateRoles([role]));
        }
    }

    [Fact]
    public void AssignableRoles_IncludeTheDocumentedRoleCodes()
    {
        string[] expected = ["Admin", "Auditor", "JiraPublisher", "Lead", "Operator", "ReadOnly", "ResourceCurator"];

        Assert.Equal(expected, AccessDecisionRules.AssignableRoles);
    }
}
