using FluentAssertions;
using SecureOps.Api.Validation;
using SecureOps.Shared.Contracts.Identity;

namespace SecureOps.Tests.Integration.Api;

public sealed class IdentityLookupRequestValidatorTests
{
    [Fact]
    public void Validate_WhenPurposeMissingOrBlank_Succeeds()
    {
        IdentityLookupRequestValidator validator = new();

        FluentValidation.Results.ValidationResult missing = validator.Validate(new IdentityLookupRequest("pam12356"));
        FluentValidation.Results.ValidationResult blank = validator.Validate(new IdentityLookupRequest("pam12356", "  "));

        missing.IsValid.Should().BeTrue();
        blank.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("oversized")]
    [InlineData("control")]
    public void Validate_WhenOptionalPurposeIsUnsafe_Fails(string kind)
    {
        IdentityLookupRequestValidator validator = new();
        string purpose = kind == "oversized" ? new string('x', 501) : "context\u0001continuation";

        FluentValidation.Results.ValidationResult result = validator.Validate(
            new IdentityLookupRequest("pam12356", purpose));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(x => x.PropertyName).Should().Contain(nameof(IdentityLookupRequest.Purpose));
    }
}
