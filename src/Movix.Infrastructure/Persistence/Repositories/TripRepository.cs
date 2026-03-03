using Microsoft.EntityFrameworkCore;
using Movix.Application.Trips;
using Movix.Domain.Entities;

namespace Movix.Infrastructure.Persistence.Repositories;

public class TripRepository : ITripRepository
{
    private readonly MovixDbContext _db;

    public TripRepository(MovixDbContext db)
    {
        _db = db;
    }

    public async Task<Trip?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.Trips
            .Include(t => t.StatusHistory)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<Trip?> GetByIdWithDriverAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.Trips
            .Include(t => t.Driver!)
            .ThenInclude(d => d!.Vehicles)
            .Include(t => t.StatusHistory)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public Task AddAsync(Trip trip, CancellationToken cancellationToken = default)
    {
        _db.Trips.Add(trip);
        return Task.CompletedTask;
    }

    /// <summary>
    /// BUG-002 fix: Add new status history via DbSet so EF marks the entity as Added (INSERT).
    /// Adding via trip.StatusHistory.Add() when the trip was loaded with Include(StatusHistory)
    /// causes EF change tracker to treat the new row as Modified, generating UPDATE instead of
    /// INSERT; 0 rows affected triggers DbUpdateConcurrencyException and 409. Explicit Add
    /// to TripStatusHistories ensures INSERT semantics in the same UnitOfWork as the trip update.
    /// </summary>
    public Task AddStatusHistoryAsync(TripStatusHistory history, CancellationToken cancellationToken = default)
    {
        _db.TripStatusHistories.Add(history);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Trip>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        return await _db.Trips
            .OrderByDescending(t => t.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }
}
