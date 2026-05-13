using CritCrit.Api.Org.Endpoints;
using FluentValidation;

namespace CritCrit.Api.Org.Validators;

public sealed class CreatePlainOrgNodeRequestValidator : AbstractValidator<CreatePlainOrgNodeRequest>
{
    public CreatePlainOrgNodeRequestValidator()
    {
        RuleFor(x => x.ParentId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}
