using FluentAssertions;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.Identity;

public sealed class IdentityProviderInputGuardTests
{
    [Fact]
    public void EnsureSafeExactAccount_WhenExactSamAccount_DoesNotThrow()
    {
        Action act = () => IdentityProviderInputGuard.EnsureSafeExactAccount("pam12356", new IdentityLookupOptions());

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureSafeExactAccount_WhenExactUpn_DoesNotThrow()
    {
        Action act = () => IdentityProviderInputGuard.EnsureSafeExactAccount("pam12356@contoso.local", new IdentityLookupOptions());

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("pam*")]
    [InlineData("pam12356,pam78900")]
    [InlineData("pam12356 pam78900")]
    [InlineData("(&(objectClass=user))")]
    [InlineData("CONTOSO\\pam12356")]
    public void EnsureSafeExactAccount_WhenSearchOrBulkInput_Throws(string account)
    {
        Action act = () => IdentityProviderInputGuard.EnsureSafeExactAccount(account, new IdentityLookupOptions());

        act.Should().Throw<IdentityProviderInputRejectedException>();
    }

    [Fact]
    public void EnsureSafeExactAccount_WhenTooLong_Throws()
    {
        Action act = () => IdentityProviderInputGuard.EnsureSafeExactAccount(
            "pam12356",
            new IdentityLookupOptions { MaxAccountLength = 5 });

        act.Should().Throw<IdentityProviderInputRejectedException>()
            .Which.Code.Should().Be("AccountTooLong");
    }
}
