using MediatR;
using Movix.Application.Common.Models;
using Movix.Application.Pricing.Commands.CreateTariffPlan;

namespace Movix.Application.Pricing.Commands.UpdateTariffPlan;

public record UpdateTariffPlanCommand(
    Guid TenantId,
    Guid TariffPlanId,
    string Name,
    string Currency,
    decimal BaseFare,
    decimal PricePerKm,
    decimal PricePerMinute,
    decimal MinimumFare,
    DateTime? EffectiveFromUtc = null) : IRequest<Result<TariffPlanDto>>;
