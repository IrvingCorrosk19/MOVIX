using Microsoft.EntityFrameworkCore;
using Movix.Application.Pricing;
using Movix.Domain.Entities;
using Movix.Infrastructure.Persistence;

namespace Movix.Infrastructure.Persistence.Repositories;

public class TariffPlanRepository : ITariffPlanRepository
{
    private readonly MovixDbContext _db;

    public TariffPlanRepository(MovixDbContext db)
    {
        _db = db;
    }

    public async Task<TariffPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.TariffPlans.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<TariffPlan?> GetApplicableTariffAsync(Guid tenantId, DateTime atUtc, CancellationToken cancellationToken = default)
    {
        return await _db.TariffPlans
            .Where(x => x.TenantId == tenantId && x.IsActive
                && (x.EffectiveFromUtc == null || x.EffectiveFromUtc <= atUtc)
                && (x.EffectiveUntilUtc == null || x.EffectiveUntilUtc > atUtc))
            .OrderBy(x => x.Priority)
            .ThenByDescending(x => x.EffectiveFromUtc ?? DateTime.MinValue)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> HasOverlappingSamePriorityAsync(Guid tenantId, Guid planId, int priority, DateTime? effectiveFromUtc, DateTime? effectiveUntilUtc, CancellationToken cancellationToken = default)
    {
        var query = _db.TariffPlans
            .Where(x => x.TenantId == tenantId && x.IsActive && x.Id != planId && x.Priority == priority);
        // Overlap: [ourFrom, ourUntil) with [x.From, x.Until)
        query = query.Where(x =>
            (x.EffectiveUntilUtc == null || effectiveFromUtc == null || effectiveFromUtc < x.EffectiveUntilUtc)
            && (effectiveUntilUtc == null || x.EffectiveFromUtc == null || effectiveUntilUtc > x.EffectiveFromUtc));
        return await query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TariffPlan>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _db.TariffPlans
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(TariffPlan tariffPlan, CancellationToken cancellationToken = default)
    {
        await _db.TariffPlans.AddAsync(tariffPlan, cancellationToken);
    }
}
