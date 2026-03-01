using FluentValidation;
using Movix.Domain.Enums;

namespace Movix.Application.Trips.Commands.TransitionTrip;

public class TransitionTripCommandValidator : AbstractValidator<TransitionTripCommand>
{
    public TransitionTripCommandValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.TargetStatus).IsInEnum();
        RuleFor(x => x.Reason).MaximumLength(500).When(x => x.Reason != null);
    }
}
