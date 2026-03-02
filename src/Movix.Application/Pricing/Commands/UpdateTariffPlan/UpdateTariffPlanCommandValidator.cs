using FluentValidation;

namespace Movix.Application.Pricing.Commands.UpdateTariffPlan;

public class UpdateTariffPlanCommandValidator : AbstractValidator<UpdateTariffPlanCommand>
{
    public UpdateTariffPlanCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.TariffPlanId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.BaseFare).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PricePerKm).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PricePerMinute).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MinimumFare).GreaterThanOrEqualTo(0);
    }
}
