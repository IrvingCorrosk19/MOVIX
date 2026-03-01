using MediatR;
using Movix.Application.Common.Models;

namespace Movix.Application.Drivers.Commands.DriverLocation;

public record DriverLocationCommand(double Latitude, double Longitude, double? Heading = null, double? Speed = null) : IRequest<Result>;
