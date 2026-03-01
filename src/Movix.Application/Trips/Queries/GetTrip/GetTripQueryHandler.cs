using MediatR;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;
using Movix.Domain.Enums;

namespace Movix.Application.Trips.Queries.GetTrip;

public class GetTripQueryHandler : IRequestHandler<GetTripQuery, Result<TripDetailDto>>
{
    private readonly ITripRepository _tripRepository;
    private readonly ICurrentUserService _currentUser;

    public GetTripQueryHandler(ITripRepository tripRepository, ICurrentUserService currentUser)
    {
        _tripRepository = tripRepository;
        _currentUser = currentUser;
    }

    public async Task<Result<TripDetailDto>> Handle(GetTripQuery request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null)
            return Result<TripDetailDto>.Failure("Trip not found", "TRIP_NOT_FOUND");

        var userId = _currentUser.UserId;
        var role = _currentUser.Role;
        var isOwner = userId == trip.PassengerId || userId == trip.DriverId;
        var isAdminOrSupport = role == Role.Admin || role == Role.Support;
        if (!isOwner && !isAdminOrSupport)
            return Result<TripDetailDto>.Failure("Forbidden", "FORBIDDEN");

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
