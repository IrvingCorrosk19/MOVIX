using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Movix.Application.Admin.Queries.GetDrivers;
using Movix.Application.Admin.Queries.GetTrips;

namespace Movix.Api.Controllers.V1;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Admin,Support")]
public class AdminController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("trips")]
    public async Task<IActionResult> GetTrips([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetAdminTripsQuery(page, pageSize), ct);
        if (!result.Succeeded)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpGet("drivers")]
    public async Task<IActionResult> GetDrivers([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetAdminDriversQuery(page, pageSize), ct);
        if (!result.Succeeded)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }
}
