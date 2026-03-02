using MediatR;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;
using Movix.Domain.Enums;

namespace Movix.Application.Trips.Queries.GetTrip;

public class GetTripQueryHandler : IRequestHandler<GetTripQuery, Result<TripDetailDto>>
{
    private readonly ITripRepository _tripRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ITenantContext _tenantContext;

    public GetTripQueryHandler(
        ITripRepository tripRepository,
        ICurrentUserService currentUser,
        ITenantContext tenantContext)
    {
        _tripRepository = tripRepository;
        _currentUser = currentUser;
        _tenantContext = tenantContext;
    }

    public async Task<Result<TripDetailDto>> Handle(GetTripQuery request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null)
            return Result<TripDetailDto>.Failure("Trip not found", "TRIP_NOT_FOUND");

        var userId = _currentUser.UserId;
        var role = _currentUser.Role;
        var isOwner = userId == trip.PassengerId || userId == trip.DriverId;
        var isAdminOrSupport = role == Role.Admin || role == Role.Support || role == Role.SuperAdmin;

        if (!isOwner && !isAdminOrSupport)
            return Result<TripDetailDto>.Failure("Forbidden", "FORBIDDEN");

        // Admin/Support cross-tenant guard: they can only access trips from their own tenant.
        // SuperAdmin can cross tenants (they would have set X-Tenant-Id in the middleware if needed).
        if (isAdminOrSupport && !isOwner && !_tenantContext.IsSuperAdmin)
        {
            if (trip.TenantId.HasValue && trip.TenantId != _tenantContext.TenantId)
                return Result<TripDetailDto>.Failure("Trip not found", "TRIP_NOT_FOUND");
        }

        var dto = new TripDetailDto(
            trip.Id,
            trip.Status.ToString(),
            trip.PassengerId,
            trip.DriverId,
            trip.PickupAddress,
            trip.DropoffAddress,
            trip.EstimatedAmount,
            trip.FinalAmount,
            trip.Currency,
            trip.CreatedAtUtc,
            trip.UpdatedAtUtc);

        return Result<TripDetailDto>.Success(dto);
    }
}
