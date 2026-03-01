using MediatR;
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

        var userId = _currentUser.UserId ?? Guid.Empty;
        var now = _dateTime.UtcNow;
        var fromStatus = trip.Status;

        trip.Status = request.TargetStatus;
        trip.UpdatedAtUtc = now;
        trip.UpdatedBy = userId.ToString();

        trip.StatusHistory.Add(new TripStatusHistory
        {
            Id = Guid.NewGuid(),
            TripId = trip.Id,
            FromStatus = fromStatus,
            ToStatus = request.TargetStatus,
            Reason = request.Reason,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = userId.ToString(),
            UpdatedBy = userId.ToString()
        });

        await _uow.SaveChangesAsync(cancellationToken);
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
