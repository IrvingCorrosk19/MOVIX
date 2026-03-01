using MediatR;
using Movix.Application.Common.Models;

namespace Movix.Application.Trips.Commands.CreateTrip;

public record CreateTripCommand(
    string IdempotencyKey,
    double PickupLatitude,
    double PickupLongitude,
    double DropoffLatitude,
    double DropoffLongitude,
    string? PickupAddress,
    string? DropoffAddress,
    decimal? EstimatedAmount,
    string Currency = "USD") : IRequest<Result<TripDto>>;

public record TripDto(
    Guid Id,
    string Status,
    string? PickupAddress,
    string? DropoffAddress,
    decimal? EstimatedAmount,
    decimal? FinalAmount,
    string Currency,
    DateTime CreatedAtUtc);
