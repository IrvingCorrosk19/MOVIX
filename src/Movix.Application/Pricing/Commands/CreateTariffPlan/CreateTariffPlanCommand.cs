using MediatR;
using Movix.Application.Common.Models;

namespace Movix.Application.Pricing.Commands.CreateTariffPlan;

public record CreateTariffPlanCommand(
    Guid TenantId,
    string Name,
    string Currency,
    decimal BaseFare,
    decimal PricePerKm,
    decimal PricePerMinute,
    decimal MinimumFare,
    int Priority = 100,
    DateTime? EffectiveFromUtc = null,
    DateTime? EffectiveUntilUtc = null) : IRequest<Result<TariffPlanDto>>;

public record TariffPlanDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string Currency,
    decimal BaseFare,
    decimal PricePerKm,
    decimal PricePerMinute,
    decimal MinimumFare,
    bool IsActive,
    int Priority,
    DateTime? EffectiveFromUtc,
    DateTime? EffectiveUntilUtc,
    DateTime CreatedAtUtc);
