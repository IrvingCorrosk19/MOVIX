using Movix.Domain.Entities;

namespace Movix.Application.Drivers;

public interface IDriverLocationRepository
{
    Task AddAsync(DriverLocationLive location, CancellationToken cancellationToken = default);
}
