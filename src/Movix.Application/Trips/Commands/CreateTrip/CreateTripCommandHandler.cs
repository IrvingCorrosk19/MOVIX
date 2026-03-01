using Movix.Domain.Entities;
using MediatR;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;
using Movix.Domain.Enums;
using Movix.Domain.Trip;
using NetTopologySuite.Geometries;

namespace Movix.Application.Trips.Commands.CreateTrip;

public class CreateTripCommandHandler : IRequestHandler<CreateTripCommand, Result<TripDto>>
{
    private readonly ITripRepository _tripRepository;
    private readonly IIdempotencyService _idempotencyService;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeService _dateTime;
    private readonly IUnitOfWork _uow;

    public CreateTripCommandHandler(
        ITripRepository tripRepository,
        IIdempotencyService idempotencyService,
        ICurrentUserService currentUser,
        IDateTimeService dateTime,
        IUnitOfWork uow)
    {
        _tripRepository = tripRepository;
        _idempotencyService = idempotencyService;
        _currentUser = currentUser;
        _dateTime = dateTime;
        _uow = uow;
    }

    public async Task<Result<TripDto>> Handle(CreateTripCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue)
            return Result<TripDto>.Failure("Unauthorized", "UNAUTHORIZED");

        var existing = await _idempotencyService.GetResponseAsync(request.IdempotencyKey, cancellationToken);
        if (existing != null)
        {
            var existingId = Guid.Parse(existing);
            var trip = await _tripRepository.GetByIdAsync(existingId, cancellationToken);
            if (trip != null)
                return Result<TripDto>.Success(Map(trip));
        }

        var pickup = new Point(new NetTopologySuite.Geometries.Coordinate(request.PickupLongitude, request.PickupLatitude)) { SRID = 4326 };
        var dropoff = new Point(new NetTopologySuite.Geometries.Coordinate(request.DropoffLongitude, request.DropoffLatitude)) { SRID = 4326 };
        var now = _dateTime.UtcNow;

        var tripEntity = new Trip
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = request.IdempotencyKey,
            PassengerId = userId.Value,
            Status = TripStatus.Requested,
            PickupLocation = pickup,
            DropoffLocation = dropoff,
            PickupAddress = request.PickupAddress,
            DropoffAddress = request.DropoffAddress,
            EstimatedAmount = request.EstimatedAmount,
            Currency = request.Currency,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = userId.ToString(),
            UpdatedBy = userId.ToString(),
            RowVersion = new byte[] { 1 }
        };

        tripEntity.StatusHistory.Add(new TripStatusHistory
        {
            Id = Guid.NewGuid(),
            TripId = tripEntity.Id,
            FromStatus = TripStatus.Requested,
            ToStatus = TripStatus.Requested,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = userId.ToString(),
            UpdatedBy = userId.ToString()
        });

        await _tripRepository.AddAsync(tripEntity, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        await _idempotencyService.StoreAsync(request.IdempotencyKey, tripEntity.Id.ToString(), cancellationToken);

        return Result<TripDto>.Success(Map(tripEntity));
    }

    private static TripDto Map(Trip t) => new(
        t.Id,
        t.Status.ToString(),
        t.PickupAddress,
        t.DropoffAddress,
        t.EstimatedAmount,
        t.FinalAmount,
        t.Currency,
        t.CreatedAtUtc);
}
