using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Movix.Application.Payments.Commands.CreatePayment;

namespace Movix.Api.Controllers.V1;

[ApiController]
[Route("api/v1/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PaymentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    [EnableRateLimiting("payments")]
    public async Task<IActionResult> Create([FromBody] CreatePaymentRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest(new { error = "Idempotency-Key header is required", code = "IDEMPOTENCY_KEY_REQUIRED" });

        var result = await _mediator.Send(new CreatePaymentCommand(
            idempotencyKey,
            request.TripId,
            request.Amount,
            request.Currency ?? "USD"), ct);
        if (!result.Succeeded)
            return BadRequest(new { error = result.Error, code = result.ErrorCode });
        return Ok(result.Data);
    }
}

public record CreatePaymentRequest(Guid TripId, decimal Amount, string? Currency);
