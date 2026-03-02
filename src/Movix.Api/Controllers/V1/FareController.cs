using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Movix.Api.Filters;
using Movix.Application.Common.Interfaces;
using Movix.Application.Pricing.Queries.QuoteFare;

namespace Movix.Api.Controllers.V1;

[ApiController]
[Route("api/v1/fare")]
[Authorize]
[RequireTenant]
public class FareController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public FareController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpGet("quote")]
    public async Task<IActionResult> Quote([FromQuery] decimal distanceKm, [FromQuery] decimal durationMin, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId!.Value;
        var result = await _mediator.Send(new QuoteFareQuery(tenantId, distanceKm, durationMin), ct);
        if (!result.Succeeded)
            return result.ErrorCode is "NO_ACTIVE_TARIFF" or "NO_APPLICABLE_TARIFF" ? NotFound(new { error = result.Error, code = result.ErrorCode }) : BadRequest(new { error = result.Error, code = result.ErrorCode });
        return Ok(result.Data);
    }
}
