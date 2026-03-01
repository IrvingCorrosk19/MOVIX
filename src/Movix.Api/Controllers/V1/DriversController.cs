using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Movix.Application.Drivers.Commands.DriverLocation;
using Movix.Application.Drivers.Commands.DriverOnboarding;
using Movix.Application.Drivers.Commands.DriverStatus;
using Movix.Domain.Enums;

namespace Movix.Api.Controllers.V1;

[ApiController]
[Route("api/v1/drivers")]
[Authorize(Roles = "Driver,Admin")]
public class DriversController : ControllerBase
{
    private readonly IMediator _mediator;

    public DriversController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("onboarding")]
    public async Task<IActionResult> Onboarding([FromBody] DriverOnboardingRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new DriverOnboardingCommand(
            request.LicenseNumber,
            request.VehiclePlate,
            request.VehicleModel,
            request.VehicleColor), ct);
        if (!result.Succeeded)
            return BadRequest(new { error = result.Error, code = result.ErrorCode });
        return Ok(result.Data);
    }

    [HttpPost("status")]
    [EnableRateLimiting("trips")]
    public async Task<IActionResult> Status([FromBody] DriverStatusRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new DriverStatusCommand(request.Status), ct);
        if (!result.Succeeded)
            return BadRequest(new { error = result.Error, code = result.ErrorCode });
        return Ok();
    }

    [HttpPost("location")]
    [EnableRateLimiting("trips")]
    public async Task<IActionResult> Location([FromBody] DriverLocationRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new DriverLocationCommand(
            request.Latitude,
            request.Longitude,
            request.Heading,
            request.Speed), ct);
        if (!result.Succeeded)
            return BadRequest(new { error = result.Error, code = result.ErrorCode });
        return Ok();
    }
}

public record DriverOnboardingRequest(string? LicenseNumber, string? VehiclePlate, string? VehicleModel, string? VehicleColor);
public record DriverStatusRequest(DriverStatus Status);
public record DriverLocationRequest(double Latitude, double Longitude, double? Heading, double? Speed);
