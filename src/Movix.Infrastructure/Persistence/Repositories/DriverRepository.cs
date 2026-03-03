using Microsoft.EntityFrameworkCore;
using Movix.Application.Common.Interfaces;
using Movix.Application.Drivers;
using Movix.Domain.Entities;
using Movix.Infrastructure.Persistence.Extensions;

namespace Movix.Infrastructure.Persistence.Repositories;

public class DriverRepository : IDriverRepository
{
    private readonly MovixDbContext _db;
    private readonly ITenantContext _tenantContext;

    public DriverRepository(MovixDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<Guid?> GetDriverIdByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _db.Drivers
            .Where(d => d.UserId == userId)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Driver?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _db.Drivers
            .FirstOrDefaultAsync(d => d.UserId == userId, cancellationToken);
    }

    public async Task<Driver?> GetByUserIdWithVehiclesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _db.Drivers
            .Include(d => d.Vehicles)
            .FirstOrDefaultAsync(d => d.UserId == userId, cancellationToken);
    }

    public async Task<Driver?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.Drivers.FindAsync(new object[] { id }, cancellationToken);
    }

    public Task AddAsync(Driver driver, CancellationToken cancellationToken = default)
    {
        _db.Drivers.Add(driver);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Driver driver, CancellationToken cancellationToken = default)
    {
        _db.Drivers.Update(driver);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns paged drivers scoped to the caller's tenant (auto-applied via ITenantContext).
    /// SuperAdmin bypasses the filter and sees all drivers.
    /// </summary>
    public async Task<(IReadOnlyList<Driver> Drivers, int TotalCount)> GetPagedAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _db.Drivers
            .ApplyTenantScope(_tenantContext)
            .OrderByDescending(d => d.CreatedAtUtc);

        var total = await query.CountAsync(cancellationToken);
        var drivers = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (drivers, total);
    }
}
