using Movix.Domain.Entities;

namespace Movix.Application.Trips;

public interface ITripRepository
{
    Task<Trip?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Trip?> GetByIdWithDriverAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Trip trip, CancellationToken cancellationToken = default);
    Task AddStatusHistoryAsync(TripStatusHistory history, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Trip>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}
