using MediatR;
using Movix.Application.Common.Models;
using Movix.Application.Pricing.Commands.CreateTariffPlan;

namespace Movix.Application.Pricing.Queries.GetActiveTariffPlan;

public record GetActiveTariffPlanQuery(Guid TenantId) : IRequest<Result<TariffPlanDto>>;
