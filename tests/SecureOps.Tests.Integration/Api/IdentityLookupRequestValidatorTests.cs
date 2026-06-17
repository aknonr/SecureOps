using FluentAssertions;
using SecureOps.Api.Validation;
using SecureOps.Shared.Contracts.Identity;

namespace SecureOps.Tests.Integration.Api;

public sealed class IdentityLookupRequestValidatorTests
{
    [Fact]
    public void Validate_WhenPurposeEmpty_Fails()
    {
        IdentityLookupRequestValidator validator = new();

        FluentValidation.Results.ValidationResult result = validator.Validate(new IdentityLookupRequest("pam12356", ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(x => x.PropertyName).Should().Contain(nameof(IdentityLookupRequest.Purpose));
    }
}
