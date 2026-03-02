using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;
using Movix.Application.Pricing.Commands.ActivateTariffPlan;
using Movix.Application.Pricing.Commands.CreateTariffPlan;
using Movix.Application.Pricing.Commands.UpdateTariffPlan;
using Movix.Application.Pricing.Queries.GetActiveTariffPlan;
using Movix.Application.Pricing.Queries.QuoteFare;
using Movix.Api.Filters;

namespace Movix.Api.Controllers.V1;

[ApiController]
[Route("api/v1/admin/tariffs")]
[Authorize(Roles = "Admin,Support")]
[RequireTenant]
public class TariffsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public TariffsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTariffRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId!.Value;
        var result = await _mediator.Send(new CreateTariffPlanCommand(
            tenantId, request.Name, request.Currency, request.BaseFare, request.PricePerKm,
            request.PricePerMinute, request.MinimumFare, request.Priority, request.EffectiveFromUtc, request.EffectiveUntilUtc), ct);
        if (!result.Succeeded)
            return BadRequest(new { error = result.Error, code = result.ErrorCode });
        return Ok(result.Data);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTariffRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId!.Value;
        var result = await _mediator.Send(new UpdateTariffPlanCommand(
            tenantId, id, request.Name, request.Currency, request.BaseFare, request.PricePerKm,
            request.PricePerMinute, request.MinimumFare, request.EffectiveFromUtc), ct);
        if (!result.Succeeded)
            return result.ErrorCode == "TARIFF_NOT_FOUND" ? NotFound(new { error = result.Error, code = result.ErrorCode })
                : result.ErrorCode == "TARIFF_CROSS_TENANT" ? (IActionResult)Forbid() : BadRequest(new { error = result.Error, code = result.ErrorCode });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId!.Value;
        var result = await _mediator.Send(new ActivateTariffPlanCommand(tenantId, id), ct);
        if (!result.Succeeded)
            return result.ErrorCode == "TARIFF_NOT_FOUND" ? NotFound(new { error = result.Error, code = result.ErrorCode })
                : result.ErrorCode == "TARIFF_CROSS_TENANT" ? (IActionResult)Forbid()
                : result.ErrorCode == "TARIFF_PRIORITY_CONFLICT" ? BadRequest(new { error = result.Error, code = result.ErrorCode })
                : BadRequest(new { error = result.Error, code = result.ErrorCode });
        return Ok(result.Data);
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActive(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId!.Value;
        var result = await _mediator.Send(new GetActiveTariffPlanQuery(tenantId), ct);
        if (!result.Succeeded)
            return result.ErrorCode is "NO_ACTIVE_TARIFF" or "NO_APPLICABLE_TARIFF" ? NotFound(new { error = result.Error, code = result.ErrorCode }) : BadRequest(new { error = result.Error, code = result.ErrorCode });
        return Ok(result.Data);
    }

    [HttpGet("fare/quote")]
    public async Task<IActionResult> QuoteFare([FromQuery] decimal distanceKm, [FromQuery] decimal durationMin, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId!.Value;
        var result = await _mediator.Send(new QuoteFareQuery(tenantId, distanceKm, durationMin), ct);
        if (!result.Succeeded)
            return result.ErrorCode is "NO_ACTIVE_TARIFF" or "NO_APPLICABLE_TARIFF" ? NotFound(new { error = result.Error, code = result.ErrorCode }) : BadRequest(new { error = result.Error, code = result.ErrorCode });
        return Ok(result.Data);
    }
}

public record CreateTariffRequest(string Name, string Currency, decimal BaseFare, decimal PricePerKm, decimal PricePerMinute, decimal MinimumFare, int Priority = 100, DateTime? EffectiveFromUtc = null, DateTime? EffectiveUntilUtc = null);
public record UpdateTariffRequest(string Name, string Currency, decimal BaseFare, decimal PricePerKm, decimal PricePerMinute, decimal MinimumFare, DateTime? EffectiveFromUtc = null);
