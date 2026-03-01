using Movix.Domain.Entities;

namespace Movix.Application.Drivers;

public interface IDriverRepository
{
    Task<Driver?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Driver?> GetByUserIdWithVehiclesAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Driver?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Driver driver, CancellationToken cancellationToken = default);
    Task UpdateAsync(Driver driver, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Driver> Drivers, int TotalCount)> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}
