using FluentValidation;

namespace Movix.Application.Auth.Commands.Register;

public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(100)
            .Matches(@"\d")
                .WithMessage("Password must contain at least one number.")
            .Matches(@"[A-Z]")
                .WithMessage("Password must contain at least one uppercase letter.");
    }
}
