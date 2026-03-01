using MediatR;
using Movix.Application.Common.Models;
using Movix.Application.Trips.Commands.CreateTrip;

namespace Movix.Application.Trips.Commands.AcceptTrip;

public record AcceptTripCommand(Guid TripId, Guid VehicleId) : IRequest<Result<TripDto>>;
