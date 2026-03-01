using MediatR;
using Movix.Application.Common.Exceptions;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;
using Movix.Application.Trips.Commands.CreateTrip;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Movix.Domain.Trip;

namespace Movix.Application.Trips.Commands.TransitionTrip;

public class TransitionTripCommandHandler : IRequestHandler<TransitionTripCommand, Result<TripDto>>
{
    private readonly ITripRepository _tripRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeService _dateTime;
    private readonly IUnitOfWork _uow;

    public TransitionTripCommandHandler(
        ITripRepository tripRepository,
        ICurrentUserService currentUser,
        IDateTimeService dateTime,
        IUnitOfWork uow)
    {
        _tripRepository = tripRepository;
        _currentUser = currentUser;
        _dateTime = dateTime;
        _uow = uow;
    }

    public async Task<Result<TripDto>> Handle(TransitionTripCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdWithDriverAsync(request.TripId, cancellationToken);
        if (trip == null)
            return Result<TripDto>.Failure("Trip not found", "TRIP_NOT_FOUND");

        if (!TripStateMachine.CanTransition(trip.Status, request.TargetStatus))
            return Result<TripDto>.Failure($"Invalid transition from {trip.Status} to {request.TargetStatus}", "INVALID_TRANSITION");

        // ABAC — R-1 / R-2
        var userId = _currentUser.UserId;
        var role = _currentUser.Role;
        var isAdminOrSupport = role == Role.Admin || role == Role.Support;

        if (request.TargetStatus is TripStatus.DriverArrived or TripStatus.InProgress or TripStatus.Completed)
        {
            if (trip.DriverId == null)
                return Result<TripDto>.Failure("No driver assigned to this trip", "DRIVER_NOT_ASSIGNED");
            if (!isAdminOrSupport && userId != trip.DriverId)
                return Result<TripDto>.Failure("Forbidden", "FORBIDDEN");
        }
        else if (request.TargetStatus == TripStatus.Cancelled)
        {
            if (!isAdminOrSupport && userId != trip.PassengerId && userId != trip.DriverId)
                return Result<TripDto>.Failure("Forbidden", "FORBIDDEN");
        }

        var userIdForAudit = userId ?? Guid.Empty;
        var now = _dateTime.UtcNow;
        var fromStatus = trip.Status;

        trip.Status = request.TargetStatus;
        trip.UpdatedAtUtc = now;
        trip.UpdatedBy = userIdForAudit.ToString();

        trip.StatusHistory.Add(new TripStatusHistory
        {
            Id = Guid.NewGuid(),
            TripId = trip.Id,
            FromStatus = fromStatus,
            ToStatus = request.TargetStatus,
            Reason = request.Reason,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = userIdForAudit.ToString(),
            UpdatedBy = userIdForAudit.ToString()
        });

        try
        {
            await _uow.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            return Result<TripDto>.Failure("Concurrent modification", "CONFLICT");
        }

        return Result<TripDto>.Success(Map(trip));
    }

    private static TripDto Map(Domain.Entities.Trip t) => new(
        t.Id,
        t.Status.ToString(),
        t.PickupAddress,
        t.DropoffAddress,
        t.EstimatedAmount,
        t.FinalAmount,
        t.Currency,
        t.CreatedAtUtc);
}
