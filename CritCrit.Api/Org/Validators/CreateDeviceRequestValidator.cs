using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using FluentValidation;

namespace CritCrit.Api.Org.Validators;

public sealed class CreateDeviceRequestValidator : AbstractValidator<CreateDeviceRequest>
{
    public CreateDeviceRequestValidator()
    {
        RuleFor(x => x.ParentStoreId).NotEmpty();
        RuleFor(x => x.SerialNumber).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DeviceType).IsInEnum();
    }
}
