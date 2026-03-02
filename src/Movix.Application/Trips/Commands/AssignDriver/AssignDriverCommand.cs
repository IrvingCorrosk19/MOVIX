using MediatR;
using Movix.Application.Common.Models;
using Movix.Application.Trips.Commands.CreateTrip;

namespace Movix.Application.Trips.Commands.AssignDriver;

public record AssignDriverCommand(Guid TripId) : IRequest<Result<TripDto>>;
