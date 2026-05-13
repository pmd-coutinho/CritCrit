using CritCrit.Api.Org.Endpoints;
using FluentValidation;

namespace CritCrit.Api.Org.Validators;

public sealed class HardDeleteOrgNodeRequestValidator : AbstractValidator<HardDeleteOrgNodeRequest>
{
    public HardDeleteOrgNodeRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
