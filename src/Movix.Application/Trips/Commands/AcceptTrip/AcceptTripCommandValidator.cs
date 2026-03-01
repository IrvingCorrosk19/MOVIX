using FluentValidation;

namespace Movix.Application.Trips.Commands.AcceptTrip;

public class AcceptTripCommandValidator : AbstractValidator<AcceptTripCommand>
{
    public AcceptTripCommandValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.VehicleId).NotEmpty();
    }
}
