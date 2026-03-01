using MediatR;
using Movix.Application.Common.Models;

namespace Movix.Application.Drivers.Commands.DriverOnboarding;

public record DriverOnboardingCommand(
    string? LicenseNumber,
    string? VehiclePlate,
    string? VehicleModel,
    string? VehicleColor) : IRequest<Result<DriverOnboardingResponse>>;

public record DriverOnboardingResponse(Guid DriverId, bool IsVerified);
