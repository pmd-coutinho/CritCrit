using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using FluentValidation;

namespace CritCrit.Api.Org.Validators;

public class DowngradeOwnerRequestValidator : AbstractValidator<DowngradeOwnerRequest>
{
    public DowngradeOwnerRequestValidator()
    {
        RuleFor(x => x.NewRole)
            .NotEqual(OrgRole.Owner)
            .WithMessage("Downgrade target role cannot be Owner.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
