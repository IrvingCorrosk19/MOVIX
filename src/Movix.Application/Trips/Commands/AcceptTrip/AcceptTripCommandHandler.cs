using MediatR;
using Movix.Application.Common.Exceptions;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;
using Movix.Application.Drivers;
using Movix.Application.Trips.Commands.CreateTrip;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Movix.Domain.Trip;

namespace Movix.Application.Trips.Commands.AcceptTrip;

public class AcceptTripCommandHandler : IRequestHandler<AcceptTripCommand, Result<TripDto>>
{
    private readonly ITripRepository _tripRepository;
    private readonly IDriverRepository _driverRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeService _dateTime;
    private readonly IUnitOfWork _uow;
    private readonly IAuditService _audit;

    public AcceptTripCommandHandler(
        ITripRepository tripRepository,
        IDriverRepository driverRepository,
        ICurrentUserService currentUser,
        IDateTimeService dateTime,
        IUnitOfWork uow,
        IAuditService audit)
    {
        _tripRepository = tripRepository;
        _driverRepository = driverRepository;
        _currentUser = currentUser;
        _dateTime = dateTime;
        _uow = uow;
        _audit = audit;
    }

    public async Task<Result<TripDto>> Handle(AcceptTripCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue)
            return Result<TripDto>.Failure("Unauthorized", "UNAUTHORIZED");

        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null)
            return Result<TripDto>.Failure("Trip not found", "TRIP_NOT_FOUND");

        if (!TripStateMachine.CanTransition(trip.Status, TripStatus.Accepted))
            return Result<TripDto>.Failure("Invalid transition", "INVALID_TRANSITION");

        var driver = await _driverRepository.GetByUserIdWithVehiclesAsync(userId.Value, cancellationToken);
        if (driver == null)
            return Result<TripDto>.Failure("Driver not found", "DRIVER_NOT_FOUND");

        if (!trip.TenantId.HasValue || trip.TenantId.Value != driver.TenantId)
            return Result<TripDto>.Failure("Forbidden", "FORBIDDEN");

        var vehicle = driver.Vehicles.FirstOrDefault(v => v.Id == request.VehicleId);
        if (vehicle == null)
            return Result<TripDto>.Failure("Vehicle not found", "VEHICLE_NOT_FOUND");

        var now = _dateTime.UtcNow;
        var fromStatus = trip.Status;

        trip.DriverId = driver.Id;
        trip.VehicleId = vehicle.Id;
        trip.Status = TripStatus.Accepted;
        trip.UpdatedAtUtc = now;
        trip.UpdatedBy = userId.ToString();

        trip.StatusHistory.Add(new TripStatusHistory
        {
            Id = Guid.NewGuid(),
            TripId = trip.Id,
            FromStatus = fromStatus,
            ToStatus = TripStatus.Accepted,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = userId.ToString(),
            UpdatedBy = userId.ToString()
        });

        try
        {
            await _uow.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            return Result<TripDto>.Failure("Concurrent modification", "CONFLICT");
        }

        await _audit.LogAsync("AcceptTrip", "Trip", trip.Id, null, cancellationToken);
        return Result<TripDto>.Success(new TripDto(
            trip.Id,
            trip.Status.ToString(),
            trip.PickupAddress,
            trip.DropoffAddress,
            trip.EstimatedAmount,
            trip.FinalAmount,
            trip.Currency,
            trip.CreatedAtUtc));
    }
}
