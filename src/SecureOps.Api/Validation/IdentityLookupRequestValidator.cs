using FluentValidation;
using SecureOps.Shared.Contracts.Identity;

namespace SecureOps.Api.Validation;

/// <summary>
/// Validator for identity lookup requests.
/// </summary>
public sealed class IdentityLookupRequestValidator : AbstractValidator<IdentityLookupRequest>
{
    /// <summary>
    /// Initializes a new validator.
    /// </summary>
    public IdentityLookupRequestValidator()
    {
        RuleFor(x => x.Account)
            .NotEmpty()
            .MaximumLength(128);

        RuleFor(x => x.Purpose)
            .MaximumLength(500)
            .Must(value => string.IsNullOrWhiteSpace(value) || !value.Any(char.IsControl))
            .WithMessage("Purpose contains unsupported control characters.");

        RuleFor(x => x.TuruncuhatEvtId)
            .MaximumLength(100);
    }
}
