using MediatR;
using Movix.Application.Common.Models;

namespace Movix.Application.Trips.Queries.GetTrip;

public record GetTripQuery(Guid TripId) : IRequest<Result<TripDetailDto>>;

public record TripDetailDto(
    Guid Id,
    string Status,
    Guid PassengerId,
    Guid? DriverId,
    string? PickupAddress,
    string? DropoffAddress,
    decimal? EstimatedAmount,
    decimal? FinalAmount,
    string Currency,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
