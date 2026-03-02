using Microsoft.EntityFrameworkCore;
using Movix.Application.Common.Interfaces;
using Movix.Application.Drivers;
using Movix.Domain.Entities;
using Movix.Infrastructure.Persistence;

namespace Movix.Infrastructure.Persistence.Repositories;

public class DriverAvailabilityRepository : IDriverAvailabilityRepository
{
    private readonly MovixDbContext _db;
    private readonly ITenantContext _tenantContext;

    public DriverAvailabilityRepository(MovixDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Returns the first available driver in the caller's tenant.
    /// Includes the Driver navigation property so the handler can verify TenantId as defense-in-depth.
    /// SuperAdmin bypasses the tenant filter.
    /// </summary>
    public async Task<DriverAvailability?> GetFirstAvailableAsync(CancellationToken cancellationToken = default)
    {
        var query = _db.DriverAvailabilities
            .Include(x => x.Driver)
            .Where(x => x.IsOnline && x.CurrentTripId == null);

        // Auto-scope: only return drivers belonging to the caller's tenant.
        if (!_tenantContext.IsSuperAdmin && _tenantContext.TenantId.HasValue)
            query = query.Where(x => x.Driver.TenantId == _tenantContext.TenantId.Value);

        return await query
            .OrderBy(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<DriverAvailability?> GetByDriverIdAsync(Guid driverId, CancellationToken cancellationToken = default)
    {
        return await _db.DriverAvailabilities
            .FirstOrDefaultAsync(x => x.DriverId == driverId, cancellationToken);
    }

    public Task AddAsync(DriverAvailability entity, CancellationToken cancellationToken = default)
    {
        _db.DriverAvailabilities.Add(entity);
        return Task.CompletedTask;
    }
}
