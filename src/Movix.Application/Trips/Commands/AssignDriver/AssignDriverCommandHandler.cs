using MediatR;
using Movix.Application.Common.Exceptions;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;
using Movix.Application.Drivers;
using Movix.Application.Outbox;
using Movix.Application.Trips.Commands.CreateTrip;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Movix.Domain.Trip;

namespace Movix.Application.Trips.Commands.AssignDriver;

public class AssignDriverCommandHandler : IRequestHandler<AssignDriverCommand, Result<TripDto>>
{
    private readonly ITripRepository _tripRepository;
    private readonly IDriverAvailabilityRepository _availabilityRepository;
    private readonly IOutboxMessageRepository _outboxRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly IDateTimeService _dateTime;
    private readonly IUnitOfWork _uow;

    public AssignDriverCommandHandler(
        ITripRepository tripRepository,
        IDriverAvailabilityRepository availabilityRepository,
        IOutboxMessageRepository outboxRepository,
        ICurrentUserService currentUser,
        ITenantContext tenantContext,
        IDateTimeService dateTime,
        IUnitOfWork uow)
    {
        _tripRepository = tripRepository;
        _availabilityRepository = availabilityRepository;
        _outboxRepository = outboxRepository;
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _dateTime = dateTime;
        _uow = uow;
    }

    public async Task<Result<TripDto>> Handle(AssignDriverCommand request, CancellationToken cancellationToken)
    {
        var role = _currentUser.Role;
        var isAdminOrSupport = role == Role.Admin || role == Role.Support || role == Role.SuperAdmin;
        if (!isAdminOrSupport)
            return Result<TripDto>.Failure("Forbidden", "FORBIDDEN");

        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null)
            return Result<TripDto>.Failure("Trip not found", "TRIP_NOT_FOUND");

        if (trip.Status != TripStatus.Requested)
            return Result<TripDto>.Failure("Trip is not in Requested state", "TRIP_INVALID_STATE");

        if (!_tenantContext.IsSuperAdmin && trip.TenantId.HasValue && trip.TenantId != _tenantContext.TenantId)
            return Result<TripDto>.Failure("Forbidden", "FORBIDDEN");

        var availability = await _availabilityRepository.GetFirstAvailableAsync(cancellationToken);
        if (availability == null)
            return Result<TripDto>.Failure("No drivers available", "NO_DRIVERS_AVAILABLE");

        if (!_tenantContext.IsSuperAdmin && availability.Driver != null && trip.TenantId.HasValue && availability.Driver.TenantId != trip.TenantId.Value)
            return Result<TripDto>.Failure("Forbidden", "FORBIDDEN");

        var now = _dateTime.UtcNow;
        var userId = _currentUser.UserId?.ToString();

        var fromStatus = trip.Status;
        trip.DriverId = availability.DriverId;
        trip.Status = TripStatus.Accepted;
        trip.UpdatedAtUtc = now;
        trip.UpdatedBy = userId;

        trip.StatusHistory.Add(new TripStatusHistory
        {
            Id = Guid.NewGuid(),
            TripId = trip.Id,
            FromStatus = fromStatus,
            ToStatus = TripStatus.Accepted,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = userId,
            UpdatedBy = userId
        });

        availability.CurrentTripId = trip.Id;
        availability.UpdatedAtUtc = now;

        var payload = "{\"tripId\":\"" + trip.Id + "\",\"driverId\":\"" + availability.DriverId + "\",\"occurredAtUtc\":\"" + now.ToString("O") + "\"}";
        await _outboxRepository.AddAsync(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "DriverAssigned",
            Payload = payload,
            CreatedAtUtc = now
        }, cancellationToken);

        try
        {
            await _uow.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            return Result<TripDto>.Failure("No drivers available", "NO_DRIVERS_AVAILABLE");
        }

        return Result<TripDto>.Success(Map(trip));
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
