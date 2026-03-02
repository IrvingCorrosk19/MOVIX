using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Movix.Application.Common.Interfaces;
using Movix.Application.Payments;
using Movix.Application.Payments.Commands.CreatePayment;
using Movix.Application.Payments.Commands.ProcessStripeWebhook;

namespace Movix.Api.Controllers.V1;

[ApiController]
[Route("api/v1/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IIdempotencyService _idempotencyService;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IWebHostEnvironment _env;

    public PaymentsController(
        IMediator mediator,
        IPaymentGateway paymentGateway,
        IIdempotencyService idempotencyService,
        IPaymentRepository paymentRepository,
        IWebHostEnvironment env)
    {
        _mediator = mediator;
        _paymentGateway = paymentGateway;
        _idempotencyService = idempotencyService;
        _paymentRepository = paymentRepository;
        _env = env;
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
            return result.ErrorCode == "PAYMENT_GATEWAY_ERROR"
                ? StatusCode(503, new { error = result.Error, code = result.ErrorCode })
                : result.ErrorCode == "FORBIDDEN"
                    ? StatusCode(403, new { error = result.Error, code = result.ErrorCode })
                    : BadRequest(new { error = result.Error, code = result.ErrorCode });
        return Ok(result.Data);
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    [DisableRateLimiting]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var json = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;
        var signature = Request.Headers["Stripe-Signature"].FirstOrDefault() ?? "";
        var parseResult = await _paymentGateway.ParseAndValidateWebhookAsync(json, signature, ct);
        if (!parseResult.IsValid)
            return BadRequest(new { error = "Invalid webhook signature", code = "WEBHOOK_SIGNATURE_INVALID" });
        if (string.IsNullOrWhiteSpace(parseResult.EventId))
            return BadRequest(new { error = "Missing event id", code = "WEBHOOK_INVALID" });
        var eventKey = "stripe_evt:" + parseResult.EventId;
        if (await _idempotencyService.GetResponseAsync(eventKey, ct) != null)
            return Ok();
        var result = await _mediator.Send(new ProcessStripeWebhookCommand(parseResult), ct);
        await _idempotencyService.StoreAsync(eventKey, "1", ct);
        return Ok();
    }

    [HttpPost("simulate-webhook")]
    [AllowAnonymous]
    [DisableRateLimiting]
    public async Task<IActionResult> SimulateWebhook([FromBody] SimulateWebhookRequest request, CancellationToken ct)
    {
        if (!_env.IsDevelopment())
            return NotFound();
        if (request?.PaymentId == null || string.IsNullOrWhiteSpace(request.EventType))
            return BadRequest(new { error = "paymentId and eventType are required", code = "INVALID_REQUEST" });
        var payment = await _paymentRepository.GetByIdAsync(request.PaymentId.Value, ct);
        if (payment == null)
            return NotFound(new { error = "Payment not found", code = "PAYMENT_NOT_FOUND" });
        if (string.IsNullOrWhiteSpace(payment.ExternalPaymentId))
            return BadRequest(new { error = "Payment has no ExternalPaymentId", code = "PAYMENT_NOT_SIMULATABLE" });
        var eventId = Guid.NewGuid().ToString();
        var parseResult = new PaymentWebhookParseResult(
            true,
            eventId,
            request.EventType,
            payment.ExternalPaymentId,
            request.EventType.Contains("failed", StringComparison.OrdinalIgnoreCase) ? "Simulated" : null);
        var eventKey = "stripe_evt:" + eventId;
        if (await _idempotencyService.GetResponseAsync(eventKey, ct) != null)
            return Ok();
        await _mediator.Send(new ProcessStripeWebhookCommand(parseResult), ct);
        await _idempotencyService.StoreAsync(eventKey, "1", ct);
        return Ok();
    }
}

public record CreatePaymentRequest(Guid TripId, decimal Amount, string? Currency);

public record SimulateWebhookRequest(Guid? PaymentId, string? EventType);
