using FluentAssertions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.Identity;

public sealed class IdentityAccountNormalizerTests
{
    [Fact]
    public void Normalize_StripsDomainPrefixAndLowercases()
    {
        IdentityAccountNormalizer normalizer = CreateNormalizer();

        IdentityAccountNormalizationResult result = normalizer.Normalize(" CONTOSO\\PAM12356 ");

        result.IsValid.Should().BeTrue();
        result.NormalizedAccount.Should().Be("pam12356");
    }

    [Fact]
    public void Normalize_PreservesUpnShape()
    {
        IdentityAccountNormalizer normalizer = CreateNormalizer();

        IdentityAccountNormalizationResult result = normalizer.Normalize("PAM12356@CONTOSO.LOCAL");

        result.IsValid.Should().BeTrue();
        result.NormalizedAccount.Should().Be("pam12356@contoso.local");
    }

    [Theory]
    [InlineData("pam*")]
    [InlineData("pam12356,pam78900")]
    [InlineData("pam12356 pam78900")]
    [InlineData("(&(objectClass=user))")]
    public void Normalize_RejectsSearchOrBulkInput(string account)
    {
        IdentityAccountNormalizer normalizer = CreateNormalizer();

        IdentityAccountNormalizationResult result = normalizer.Normalize(account);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Normalize_RejectsTooLongAccount()
    {
        IdentityAccountNormalizer normalizer = CreateNormalizer(new IdentityLookupOptions { MaxAccountLength = 5 });

        IdentityAccountNormalizationResult result = normalizer.Normalize("pam12356");

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("AccountTooLong");
    }

    private static IdentityAccountNormalizer CreateNormalizer(IdentityLookupOptions? options = null)
    {
        return new IdentityAccountNormalizer(Options.Create(options ?? new IdentityLookupOptions()));
    }
}
