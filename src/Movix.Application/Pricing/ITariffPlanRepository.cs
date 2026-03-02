using Movix.Domain.Entities;

namespace Movix.Application.Pricing;

public interface ITariffPlanRepository
{
    Task<TariffPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>Deterministic: TenantId + IsActive + time window, order Priority ASC, EffectiveFromUtc DESC NULLS LAST, take 1.</summary>
    Task<TariffPlan?> GetApplicableTariffAsync(Guid tenantId, DateTime atUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TariffPlan>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task AddAsync(TariffPlan tariffPlan, CancellationToken cancellationToken = default);
    /// <summary>True if another active plan for tenant has same Priority and overlapping effective window (excluding planId).</summary>
    Task<bool> HasOverlappingSamePriorityAsync(Guid tenantId, Guid planId, int priority, DateTime? effectiveFromUtc, DateTime? effectiveUntilUtc, CancellationToken cancellationToken = default);
}
