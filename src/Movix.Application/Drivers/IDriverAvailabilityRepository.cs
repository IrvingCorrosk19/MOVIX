using Movix.Domain.Entities;

namespace Movix.Application.Drivers;

public interface IDriverAvailabilityRepository
{
    Task<DriverAvailability?> GetFirstAvailableAsync(CancellationToken cancellationToken = default);
    Task<DriverAvailability?> GetByDriverIdAsync(Guid driverId, CancellationToken cancellationToken = default);
    Task AddAsync(DriverAvailability entity, CancellationToken cancellationToken = default);
}
