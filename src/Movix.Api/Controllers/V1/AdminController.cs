using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Movix.Application.Admin.Commands.ReprocessDeadLetter;
using Movix.Application.Admin.Queries.GetDrivers;
using Movix.Application.Admin.Queries.GetTrips;
using Movix.Application.Common.Exceptions;
using Movix.Application.Common.Models;
using Movix.Application.Tenants.Commands.CreateTenant;
using Movix.Application.Tenants.Queries.GetTenants;

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

    [HttpPost("tenants")]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateTenantCommand(request.Name), ct);
        if (!result.Succeeded)
            return BadRequest(new { error = result.Error, code = result.ErrorCode });
        return Ok(result.Data);
    }

    [HttpGet("tenants")]
    public async Task<IActionResult> GetTenants(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetTenantsQuery(), ct);
        if (!result.Succeeded)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
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

    [HttpPost("outbox/{id:guid}/reprocess")]
    public async Task<IActionResult> ReprocessDeadLetter(Guid id, CancellationToken ct = default)
    {
        try
        {
            await _mediator.Send(new ReprocessDeadLetterCommand(id), ct);
            return NoContent();
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public record CreateTenantRequest(string Name);
