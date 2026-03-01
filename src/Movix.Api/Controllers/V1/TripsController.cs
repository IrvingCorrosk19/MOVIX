using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Movix.Application.Common.Models;
using Movix.Application.Trips.Commands.AcceptTrip;
using Movix.Application.Trips.Commands.CreateTrip;
using Movix.Application.Trips.Commands.TransitionTrip;
using Movix.Application.Trips.Queries.GetTrip;
using Movix.Domain.Enums;

namespace Movix.Api.Controllers.V1;

[ApiController]
[Route("api/v1/trips")]
[Authorize]
public class TripsController : ControllerBase
{
    private readonly IMediator _mediator;

    public TripsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    [EnableRateLimiting("trips")]
    public async Task<IActionResult> Create([FromBody] CreateTripRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest(new { error = "Idempotency-Key header is required", code = "IDEMPOTENCY_KEY_REQUIRED" });

        var result = await _mediator.Send(new CreateTripCommand(
            idempotencyKey,
            request.PickupLatitude,
            request.PickupLongitude,
            request.DropoffLatitude,
            request.DropoffLongitude,
            request.PickupAddress,
            request.DropoffAddress,
            request.EstimatedAmount,
            request.Currency ?? "USD"), ct);
        if (!result.Succeeded)
            return BadRequest(new { error = result.Error, code = result.ErrorCode });
        return Ok(result.Data);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetTripQuery(id), ct);
        if (!result.Succeeded)
            return MapError(result);
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/accept")]
    [Authorize(Roles = "Driver,Admin")]
    [EnableRateLimiting("trips")]
    public async Task<IActionResult> Accept(Guid id, [FromBody] AcceptTripRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new AcceptTripCommand(id, request.VehicleId), ct);
        return ToResult(result);
    }

    [HttpPost("{id:guid}/arrive")]
    [Authorize(Roles = "Driver,Admin")]
    public async Task<IActionResult> Arrive(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new TransitionTripCommand(id, TripStatus.DriverArrived), ct);
        return ToResult(result);
    }

    [HttpPost("{id:guid}/start")]
    [Authorize(Roles = "Driver,Admin")]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new TransitionTripCommand(id, TripStatus.InProgress), ct);
        return ToResult(result);
    }

    [HttpPost("{id:guid}/complete")]
    [Authorize(Roles = "Driver,Admin")]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new TransitionTripCommand(id, TripStatus.Completed), ct);
        return ToResult(result);
    }

    // Cancel: [Authorize] heredado del controlador. ABAC verificado en TransitionTripCommandHandler (passenger OR driver del viaje OR Admin/Support).
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelTripRequest? request, CancellationToken ct)
    {
        var result = await _mediator.Send(new TransitionTripCommand(id, TripStatus.Cancelled, request?.Reason), ct);
        return ToResult(result);
    }

    private IActionResult ToResult(Result<TripDto> result)
    {
        if (!result.Succeeded)
            return MapError(result);
        return Ok(result.Data);
    }

    private IActionResult MapError(Result result) => result.ErrorCode switch
    {
        "FORBIDDEN"            => StatusCode(403, new { error = result.Error, code = result.ErrorCode }),
        "TRIP_NOT_FOUND"       => NotFound(new { error = result.Error, code = result.ErrorCode }),
        "INVALID_TRANSITION"   => UnprocessableEntity(new { error = result.Error, code = result.ErrorCode }),
        "DRIVER_NOT_ASSIGNED"  => UnprocessableEntity(new { error = result.Error, code = result.ErrorCode }),
        "CONFLICT"             => Conflict(new { error = result.Error, code = result.ErrorCode }),
        _                      => BadRequest(new { error = result.Error, code = result.ErrorCode })
    };
}

public record CreateTripRequest(
    double PickupLatitude,
    double PickupLongitude,
    double DropoffLatitude,
    double DropoffLongitude,
    string? PickupAddress,
    string? DropoffAddress,
    decimal? EstimatedAmount,
    string? Currency);
public record AcceptTripRequest(Guid VehicleId);
public record CancelTripRequest(string? Reason);
