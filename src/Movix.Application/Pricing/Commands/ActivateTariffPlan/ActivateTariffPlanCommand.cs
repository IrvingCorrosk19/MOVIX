using MediatR;
using Movix.Application.Common.Models;
using Movix.Application.Pricing.Commands.CreateTariffPlan;

namespace Movix.Application.Pricing.Commands.ActivateTariffPlan;

public record ActivateTariffPlanCommand(Guid TenantId, Guid TariffPlanId) : IRequest<Result<TariffPlanDto>>;
