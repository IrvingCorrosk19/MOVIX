using FluentValidation;

namespace Movix.Application.Pricing.Commands.ActivateTariffPlan;

public class ActivateTariffPlanCommandValidator : AbstractValidator<ActivateTariffPlanCommand>
{
    public ActivateTariffPlanCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.TariffPlanId).NotEmpty();
    }
}
