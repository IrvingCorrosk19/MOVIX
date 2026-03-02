using Microsoft.EntityFrameworkCore;
using Movix.Application.Admin;
using Movix.Application.Common.Interfaces;
using Movix.Domain.Entities;
using Movix.Infrastructure.Persistence.Extensions;

namespace Movix.Infrastructure.Persistence.Repositories;

public class AdminTripRepository : IAdminTripRepository
{
    private readonly MovixDbContext _db;
    private readonly ITenantContext _tenantContext;

    public AdminTripRepository(MovixDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<(IReadOnlyList<Trip> Trips, int TotalCount)> GetPagedAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _db.Trips
            .ApplyTenantScope(_tenantContext)
            .OrderByDescending(t => t.CreatedAtUtc);

        var total = await query.CountAsync(cancellationToken);
        var trips = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (trips, total);
    }
}
