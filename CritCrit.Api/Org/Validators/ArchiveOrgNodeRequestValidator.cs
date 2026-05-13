using CritCrit.Api.Org.Endpoints;
using FluentValidation;

namespace CritCrit.Api.Org.Validators;

public sealed class ArchiveOrgNodeRequestValidator : AbstractValidator<ArchiveOrgNodeRequest>
{
    public ArchiveOrgNodeRequestValidator()
    {
        RuleFor(x => x.Force);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}
