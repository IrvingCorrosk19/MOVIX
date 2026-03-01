using FluentValidation;

namespace Movix.Application.Drivers.Commands.DriverLocation;

public class DriverLocationCommandValidator : AbstractValidator<DriverLocationCommand>
{
    public DriverLocationCommandValidator()
    {
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.Heading).InclusiveBetween(0, 360).When(x => x.Heading.HasValue);
        RuleFor(x => x.Speed).GreaterThanOrEqualTo(0).When(x => x.Speed.HasValue);
    }
}
