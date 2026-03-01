using MediatR;
using Movix.Application.Common.Models;

namespace Movix.Application.Admin.Queries.GetTrips;

public record GetAdminTripsQuery(int Page = 1, int PageSize = 20) : IRequest<Result<AdminTripsResponse>>;

public record AdminTripsResponse(IReadOnlyList<AdminTripDto> Trips, int TotalCount);

public record AdminTripDto(
    Guid Id,
    string Status,
    Guid PassengerId,
    Guid? DriverId,
    string? PickupAddress,
    string? DropoffAddress,
    decimal? FinalAmount,
    DateTime CreatedAtUtc);
