using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.DirectoryExplorer;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.Identity;

public sealed class ManagedServiceAccountLookupTests
{
    [Theory]
    [InlineData("syn.gmsa$", "syn.gmsa$")]
    [InlineData("CONTOSO\\SYN.GMSA$", "syn.gmsa$")]
    [InlineData(" syn.msa$ ", "syn.msa$")]
    public void Normalize_OneTrailingDollar_AcceptsExactSam(string input, string expected)
    {
        IOptions<IdentityLookupOptions> options = Options.Create(new IdentityLookupOptions());
        new IdentityAccountNormalizer(options).Normalize(input).NormalizedAccount.Should().Be(expected);
        Action guard = () => IdentityProviderInputGuard.EnsureSafeExactAccount(expected, options.Value);
        guard.Should().NotThrow();
        new DirectoryExactInputNormalizer(Options.Create(new DirectoryExplorerOptions()))
            .NormalizeGroup(expected).Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("$")]
    [InlineData("$syn")]
    [InlineData("syn$gmsa")]
    [InlineData("syn.gmsa$$")]
    [InlineData("syn$gmsa$")]
    [InlineData("syn.gmsa$@example.invalid")]
    [InlineData("syn.gmsa@example.invalid$")]
    [InlineData("syn.gmsa*$")]
    [InlineData("syn.gmsa?$")]
    [InlineData("syn.gmsa%$")]
    [InlineData("syn.gmsa\0$")]
    [InlineData("syn.gmsa$)(objectClass=*)")]
    [InlineData("(&(objectClass=msDS-GroupManagedServiceAccount))")]
    [InlineData("syn.gmsa$,other$")]
    public void Normalize_UnsafeDollarOrLdapInput_RejectsEvenWithPermissiveConfiguration(string input)
    {
        var options = new IdentityLookupOptions { AllowedAccountPattern = ".*" };
        new IdentityAccountNormalizer(Options.Create(options)).Normalize(input).IsValid.Should().BeFalse();
        Action guard = () => IdentityProviderInputGuard.EnsureSafeExactAccount(input, options);
        guard.Should().Throw<IdentityProviderInputRejectedException>();
        Action filter = () => ManagedServiceAccountLookup.Filter(input, options);
        filter.Should().Throw<IdentityProviderInputRejectedException>();
        new DirectoryExactInputNormalizer(Options.Create(new DirectoryExplorerOptions()))
            .NormalizeGroup(input).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("DO$MAIN\\syn.gmsa$")]
    [InlineData("DO*MAIN\\syn.gmsa$")]
    public void Normalize_UnsafeDomainPrefix_RejectsBeforeStripping(string input) =>
        new IdentityAccountNormalizer(Options.Create(new IdentityLookupOptions())).Normalize(input).IsValid.Should().BeFalse();

    [Fact]
    public void Filter_ManagedAccount_UsesExactSamAndOnlyManagedClasses()
    {
        ManagedServiceAccountLookup.Filter("syn.gmsa$", new IdentityLookupOptions()).Should().Be(
            "(&(sAMAccountName=syn.gmsa$)(|(objectClass=msDS-GroupManagedServiceAccount)(objectClass=msDS-ManagedServiceAccount)))");
        ManagedServiceAccountLookup.Attributes.Should().NotContain(attribute =>
            attribute.Contains("password", StringComparison.OrdinalIgnoreCase)
            || attribute.Equals("*", StringComparison.Ordinal)
            || attribute.Equals("msDS-GroupMSAMembership", StringComparison.OrdinalIgnoreCase));
        ManagedServiceAccountLookup.Attributes.Should().Contain("objectClass").And.Contain("sAMAccountName");
        ManagedServiceAccountLookup.Attributes.Should().NotIntersectWith(
            ["unicodePwd", "dBCSPwd", "ntPwdHistory", "lmPwdHistory", "supplementalCredentials"]);
    }

    [Theory]
    [InlineData("msDS-GroupManagedServiceAccount", "GroupManagedServiceAccount")]
    [InlineData("msDS-ManagedServiceAccount", "ManagedServiceAccount")]
    public async Task Match_ManagedClass_ProjectsSafeIdentityAndMockMatches(string objectClass, string evidence)
    {
        string account = evidence == "ManagedServiceAccount" ? "syn.msa$" : "syn.gmsa$";
        var values = new Dictionary<string, object[]>
        {
            ["sAMAccountName"] = [account.ToUpperInvariant()],
            ["objectClass"] = ["top", "person", "user", "computer", objectClass],
            ["userAccountControl"] = [512],
            ["msDS-User-Account-Control-Computed"] = [0]
        };
        ManagedServiceAccountRecord? found = ManagedServiceAccountLookup.Match(account, values);
        found!.Identity.AccountTypeEvidence.Should().Be(evidence);
        found.Identity.Enabled.Should().BeTrue();
        found.Identity.Locked.Should().BeFalse();
        JsonSerializer.Serialize(found.Identity).Should().NotContain("Password");
        DirectoryUserRecord? mock = await new MockIdentityDirectoryProvider().FindUserAsync(account, CancellationToken.None);
        mock!.AccountTypeEvidence.Should().Be(evidence);
    }

    [Theory]
    [InlineData("syn.gmsa$", "computer")]
    [InlineData("syn.gmsa$", "user")]
    [InlineData("other$", "msDS-GroupManagedServiceAccount")]
    public void Match_WrongClassOrNonExactSam_ReturnsNotFound(string returnedAccount, string objectClass)
    {
        ManagedServiceAccountLookup.Match("syn.gmsa$", new Dictionary<string, object[]>
        {
            ["sAMAccountName"] = [returnedAccount],
            ["objectClass"] = [objectClass]
        }).Should().BeNull();
    }

    [Fact]
    public void Normalize_LengthBoundary_IncludesDollarInLimit()
    {
        var normalizer = new IdentityAccountNormalizer(Options.Create(new IdentityLookupOptions { MaxAccountLength = 5 }));
        normalizer.Normalize("abcd$").IsValid.Should().BeTrue();
        normalizer.Normalize("abcde$").IsValid.Should().BeFalse();
    }
}
