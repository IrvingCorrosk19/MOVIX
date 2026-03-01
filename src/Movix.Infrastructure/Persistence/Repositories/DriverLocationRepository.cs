using Movix.Application.Drivers;
using Movix.Domain.Entities;

namespace Movix.Infrastructure.Persistence.Repositories;

public class DriverLocationRepository : IDriverLocationRepository
{
    private readonly MovixDbContext _db;

    public DriverLocationRepository(MovixDbContext db)
    {
        _db = db;
    }

    public Task AddAsync(DriverLocationLive location, CancellationToken cancellationToken = default)
    {
        _db.DriverLocationLives.Add(location);
        return Task.CompletedTask;
    }
}
