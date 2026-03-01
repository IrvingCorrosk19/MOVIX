using FluentValidation;
using Movix.Domain.Enums;

namespace Movix.Application.Drivers.Commands.DriverStatus;

public class DriverStatusCommandValidator : AbstractValidator<DriverStatusCommand>
{
    public DriverStatusCommandValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
    }
}
