using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using FluentValidation;

namespace CritCrit.Api.Org.Validators;

public class GrantOwnerRequestValidator : AbstractValidator<GrantOwnerRequest>
{
    public GrantOwnerRequestValidator()
    {
        RuleFor(x => x.SubjectId).NotEmpty();
    }
}
