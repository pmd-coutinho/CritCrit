using CritCrit.Api.Org.Endpoints;
using FluentValidation;

namespace CritCrit.Api.Org.Validators;

public sealed class CreateStoreRequestValidator : AbstractValidator<CreateStoreRequest>
{
    public CreateStoreRequestValidator()
    {
        RuleFor(x => x.ParentId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}
