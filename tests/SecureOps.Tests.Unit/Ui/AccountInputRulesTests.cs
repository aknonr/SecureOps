using FluentAssertions;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Verifies the client-side account mirror stays aligned with the server rules in
/// <c>IdentityAccountNormalizer</c>: strict enough to catch unusable input, never stricter than the
/// endpoint it posts to.
/// </summary>
public sealed class AccountInputRulesTests
{
    [Theory]
    [InlineData("pam12356")]
    [InlineData("ornek.kullanici")]
    [InlineData("first_last")]
    [InlineData("user-name")]
    [InlineData("user@contoso.com")]
    public void Validate_AcceptsRealEnterpriseAccountFormats(string account)
    {
        AccountValidationResult result = AccountInputRules.Validate(account);

        result.IsValid.Should().BeTrue($"'{account}' is accepted by the server allow-list");
        result.Message.Should().BeNull();
    }

    [Theory]
    [InlineData("CONTOSO\\pam12356", "pam12356")]
    [InlineData("contoso\\first.last", "first.last")]
    public void Validate_StripsDomainPrefixLikeTheServer(string account, string expected)
    {
        AccountValidationResult result = AccountInputRules.Validate(account);

        result.IsValid.Should().BeTrue();
        result.NormalizedPreview.Should().Be(expected);
    }

    [Fact]
    public void Validate_ExplainsNormalizationWhenTheValueChanges()
    {
        AccountValidationResult result = AccountInputRules.Validate("CONTOSO\\pam12356");

        // The operator typed one thing and the server will query another; the form says so.
        result.Advisory.Should().NotBeNull();
        result.Advisory.Should().Contain("pam12356");
    }

    [Theory]
    [InlineData("user one")]
    [InlineData("user1,user2")]
    [InlineData("user1;user2")]
    public void Validate_RejectsMoreThanOneAccount(string account)
    {
        AccountValidationResult result = AccountInputRules.Validate(account);

        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("bir hesap");
    }

    [Theory]
    [InlineData("pam*")]
    [InlineData("pam%")]
    [InlineData("(cn=admin)")]
    [InlineData("user|admin")]
    [InlineData("a?b")]
    public void Validate_RejectsWildcardAndLdapFilterInput(string account)
    {
        AccountValidationResult result = AccountInputRules.Validate(account);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_RequiresAValue(string? account)
    {
        AccountInputRules.Validate(account).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_RejectsOnlyBeyondTheServerDeclaredLength()
    {
        string atLimit = new('a', 20);
        string overLimit = new('a', 21);

        AccountInputRules.Validate(atLimit, maxAccountLength: 20).IsValid.Should().BeTrue();
        AccountInputRules.Validate(overLimit, maxAccountLength: 20).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_MeasuresLengthAfterStrippingTheDomainPrefix()
    {
        // The server strips the prefix before its length check; a mirror that measured the raw value
        // would reject accounts the endpoint accepts.
        AccountValidationResult result =
            AccountInputRules.Validate("VERYLONGDOMAINNAME\\short", maxAccountLength: 10);

        result.IsValid.Should().BeTrue();
        result.NormalizedPreview.Should().Be("short");
    }

    [Fact]
    public void Validate_AdvisesButDoesNotBlockUpnWhenUpnLookupIsDisabled()
    {
        // The server's allow-list still permits '@'; the lookup would simply not match. Blocking here
        // would be stricter than the endpoint.
        AccountValidationResult result =
            AccountInputRules.Validate("user@contoso.com", supportsUpnLookup: false);

        result.IsValid.Should().BeTrue();
        result.Advisory.Should().NotBeNull();
    }

    [Fact]
    public void Validate_RejectsCharactersOutsideTheAllowList()
    {
        AccountInputRules.Validate("kullanıcı").IsValid.Should().BeFalse();
        AccountInputRules.Validate("user#1").IsValid.Should().BeFalse();
    }
}
