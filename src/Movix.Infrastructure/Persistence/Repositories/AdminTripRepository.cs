using Microsoft.EntityFrameworkCore;
using Movix.Application.Admin;
using Movix.Domain.Entities;

namespace Movix.Infrastructure.Persistence.Repositories;

public class AdminTripRepository : IAdminTripRepository
{
    private readonly MovixDbContext _db;

    public AdminTripRepository(MovixDbContext db)
    {
        _db = db;
    }

    public async Task<(IReadOnlyList<Trip> Trips, int TotalCount)> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _db.Trips.OrderByDescending(t => t.CreatedAtUtc);
        var total = await query.CountAsync(cancellationToken);
        var trips = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (trips, total);
    }
}
