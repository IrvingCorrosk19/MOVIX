using FluentValidation;

namespace Movix.Application.Trips.Commands.CreateTrip;

public class CreateTripCommandValidator : AbstractValidator<CreateTripCommand>
{
    public CreateTripCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
        RuleFor(x => x.PickupLatitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.PickupLongitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.DropoffLatitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.DropoffLongitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.PickupAddress).MaximumLength(500);
        RuleFor(x => x.DropoffAddress).MaximumLength(500);
        RuleFor(x => x.EstimatedAmount).GreaterThanOrEqualTo(0).When(x => x.EstimatedAmount.HasValue);
        RuleFor(x => x.Currency).NotEmpty().Length(3).MaximumLength(3);
    }
}
