using CritCrit.Api.Org.Endpoints;
using FluentValidation;

namespace CritCrit.Api.Org.Validators;

public class RevokeOwnerRequestValidator : AbstractValidator<RevokeOwnerRequest>
{
    public RevokeOwnerRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
