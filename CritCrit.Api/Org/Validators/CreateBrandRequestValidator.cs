using CritCrit.Api.Org.Endpoints;
using FluentValidation;

namespace CritCrit.Api.Org.Validators;

public sealed class CreateBrandRequestValidator : AbstractValidator<CreateBrandRequest>
{
    public CreateBrandRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}
