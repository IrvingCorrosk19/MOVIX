using FluentValidation;

namespace Movix.Application.Drivers.Commands.DriverOnboarding;

public class DriverOnboardingCommandValidator : AbstractValidator<DriverOnboardingCommand>
{
    public DriverOnboardingCommandValidator()
    {
        RuleFor(x => x.LicenseNumber).MaximumLength(50).When(x => x.LicenseNumber != null);
        RuleFor(x => x.VehiclePlate).MaximumLength(20).When(x => x.VehiclePlate != null);
        RuleFor(x => x.VehicleModel).MaximumLength(100).When(x => x.VehicleModel != null);
        RuleFor(x => x.VehicleColor).MaximumLength(50).When(x => x.VehicleColor != null);
    }
}
