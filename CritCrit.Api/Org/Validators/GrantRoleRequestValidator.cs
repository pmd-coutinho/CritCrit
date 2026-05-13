using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using FluentValidation;

namespace CritCrit.Api.Org.Validators;

public sealed class GrantRoleRequestValidator : AbstractValidator<GrantRoleRequest>
{
    public GrantRoleRequestValidator()
    {
        RuleFor(x => x.OrgNodeId).NotEmpty();
        RuleFor(x => x.SubjectId).NotEmpty();
        RuleFor(x => x.Role).IsInEnum();
    }
}
