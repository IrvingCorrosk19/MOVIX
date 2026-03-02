using MediatR;
using Movix.Application.Common.Models;
using Movix.Application.Trips.Commands.CreateTrip;
using Movix.Domain.Enums;

namespace Movix.Application.Trips.Commands.TransitionTrip;

public record TransitionTripCommand(
    Guid TripId,
    TripStatus TargetStatus,
    string? Reason = null,
    decimal? DistanceKm = null,
    decimal? DurationMinutes = null) : IRequest<Result<TripDto>>;
