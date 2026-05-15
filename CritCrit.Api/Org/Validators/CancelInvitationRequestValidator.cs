using CritCrit.Api.Org.Endpoints;
using FluentValidation;

namespace CritCrit.Api.Org.Validators;

public sealed class CancelInvitationRequestValidator : AbstractValidator<CancelInvitationRequest>
{
    public CancelInvitationRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}
