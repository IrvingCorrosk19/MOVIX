using FluentValidation;

namespace Movix.Application.Trips.Commands.AssignDriver;

public class AssignDriverCommandValidator : AbstractValidator<AssignDriverCommand>
{
    public AssignDriverCommandValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
    }
}
