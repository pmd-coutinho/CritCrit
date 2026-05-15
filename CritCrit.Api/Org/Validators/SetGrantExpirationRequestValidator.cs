using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using FluentValidation;

namespace CritCrit.Api.Org.Validators;

public class SetGrantExpirationRequestValidator : AbstractValidator<SetGrantExpirationRequest>
{
    public SetGrantExpirationRequestValidator()
    {
        RuleFor(x => x.OrgNodeId).NotEmpty();
        RuleFor(x => x.SubjectId).NotEmpty();
        RuleFor(x => x.ExpiresAt)
            .Must(expiresAt => expiresAt is null || expiresAt > DateTimeOffset.UtcNow)
            .WithMessage("Expiration must be in the future.");
    }
}
