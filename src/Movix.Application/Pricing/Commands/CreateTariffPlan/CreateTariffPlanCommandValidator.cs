using FluentValidation;

namespace Movix.Application.Pricing.Commands.CreateTariffPlan;

public class CreateTariffPlanCommandValidator : AbstractValidator<CreateTariffPlanCommand>
{
    public CreateTariffPlanCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.BaseFare).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PricePerKm).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PricePerMinute).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MinimumFare).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Priority).GreaterThanOrEqualTo(0);
    }
}
