using Movix.Domain.Entities;

namespace Movix.Application.Admin;

public interface IAdminTripRepository
{
    Task<(IReadOnlyList<Trip> Trips, int TotalCount)> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}
