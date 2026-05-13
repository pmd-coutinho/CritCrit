using CritCrit.Api.Org.Endpoints;
using FluentValidation;

namespace CritCrit.Api.Org.Validators;

public sealed class MoveOrgNodeRequestValidator : AbstractValidator<MoveOrgNodeRequest>
{
    public MoveOrgNodeRequestValidator()
    {
        RuleFor(x => x.NewParentId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
