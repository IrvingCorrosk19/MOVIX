using MediatR;
using Movix.Application.Common.Models;
using Movix.Domain.Enums;

namespace Movix.Application.Drivers.Commands.DriverStatus;

public record DriverStatusCommand(Movix.Domain.Enums.DriverStatus Status) : IRequest<Result>;
