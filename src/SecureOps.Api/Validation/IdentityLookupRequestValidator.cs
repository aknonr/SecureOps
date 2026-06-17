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
            .NotEmpty()
            .MaximumLength(500);

        RuleFor(x => x.TuruncuhatEvtId)
            .MaximumLength(100);
    }
}
