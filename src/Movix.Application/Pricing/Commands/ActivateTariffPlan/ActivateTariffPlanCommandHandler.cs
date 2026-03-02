using MediatR;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;
using Movix.Application.Pricing.Commands.CreateTariffPlan;

namespace Movix.Application.Pricing.Commands.ActivateTariffPlan;

public class ActivateTariffPlanCommandHandler : IRequestHandler<ActivateTariffPlanCommand, Result<TariffPlanDto>>
{
    private readonly ITariffPlanRepository _repository;
    private readonly IUnitOfWork _uow;
    private readonly IAuditService _audit;

    public ActivateTariffPlanCommandHandler(ITariffPlanRepository repository, IUnitOfWork uow, IAuditService audit)
    {
        _repository = repository;
        _uow = uow;
        _audit = audit;
    }

    public async Task<Result<TariffPlanDto>> Handle(ActivateTariffPlanCommand request, CancellationToken cancellationToken)
    {
        var plan = await _repository.GetByIdAsync(request.TariffPlanId, cancellationToken);
        if (plan == null)
            return Result<TariffPlanDto>.Failure("Tariff plan not found", "TARIFF_NOT_FOUND");
        if (plan.TenantId != request.TenantId)
            return Result<TariffPlanDto>.Failure("Tariff plan does not belong to tenant", "TARIFF_CROSS_TENANT");

        var hasConflict = await _repository.HasOverlappingSamePriorityAsync(
            request.TenantId, plan.Id, plan.Priority, plan.EffectiveFromUtc, plan.EffectiveUntilUtc, cancellationToken);
        if (hasConflict)
            return Result<TariffPlanDto>.Failure("Another active plan with the same priority has an overlapping effective window", "TARIFF_PRIORITY_CONFLICT");

        plan.IsActive = true;

        await _uow.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("ActivateTariffPlan", "TariffPlan", plan.Id, new { plan.Priority }, cancellationToken);
        return Result<TariffPlanDto>.Success(Map(plan));
    }

    private static TariffPlanDto Map(Domain.Entities.TariffPlan p) => new(
        p.Id, p.TenantId, p.Name, p.Currency, p.BaseFare, p.PricePerKm, p.PricePerMinute, p.MinimumFare,
        p.IsActive, p.Priority, p.EffectiveFromUtc, p.EffectiveUntilUtc, p.CreatedAtUtc);
}
