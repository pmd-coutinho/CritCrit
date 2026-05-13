using CritCrit.Api.Org.Endpoints;
using FluentValidation;

namespace CritCrit.Api.Org.Validators;

public sealed class CreateSubjectRequestValidator : AbstractValidator<CreateSubjectRequest>
{
    public CreateSubjectRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.DisplayName).MaximumLength(200);
        RuleFor(x => x.Provider).NotEmpty();
        RuleFor(x => x.ProviderTenant).NotEmpty();
        RuleFor(x => x.ExternalId).NotEmpty();
    }
}
