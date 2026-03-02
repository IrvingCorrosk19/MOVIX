using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Movix.Application.Admin.Commands.ReprocessDeadLetter;
using Movix.Application.Common.Exceptions;
using Movix.Application.Payments;
using Movix.Domain.Enums;
using Movix.Infrastructure.Persistence;

namespace Movix.Api.Controllers.V1;

[ApiController]
[Route("api/v1/admin/ops")]
[Authorize(Roles = "Admin,Support")]
public class AdminOpsController : ControllerBase
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly MovixDbContext _db;
    private readonly IMediator _mediator;

    public AdminOpsController(IPaymentRepository paymentRepository, MovixDbContext db, IMediator mediator)
    {
        _paymentRepository = paymentRepository;
        _db = db;
        _mediator = mediator;
    }

    [HttpGet("payments")]
    public async Task<IActionResult> GetPayments(
        [FromQuery] Guid? tenantId,
        [FromQuery] string? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] Guid? tripId,
        CancellationToken ct)
    {
        PaymentStatus? statusEnum = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PaymentStatus>(status, true, out var parsed))
            statusEnum = parsed;
        var list = await _paymentRepository.GetFilteredAsync(tenantId, statusEnum, from, to, tripId, ct);
        var dtos = list.Select(p => new PaymentOpsDto(
            p.Id,
            p.Trip?.TenantId,
            p.TripId,
            p.PayerId,
            p.Amount,
            p.Currency,
            p.Status.ToString(),
            p.ExternalPaymentId,
            p.CreatedAtUtc)).ToList();
        return Ok(dtos);
    }

    [HttpGet("outbox")]
    public async Task<IActionResult> GetOutbox(
        [FromQuery] bool? processed,
        [FromQuery] bool? deadletter,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? type,
        CancellationToken ct)
    {
        var query = _db.OutboxMessages.AsNoTracking();
        if (processed.HasValue)
            query = processed.Value ? query.Where(x => x.ProcessedAtUtc != null) : query.Where(x => x.ProcessedAtUtc == null);
        if (deadletter.HasValue && deadletter.Value)
            query = query.Where(x => x.IsDeadLetter);
        if (from.HasValue)
            query = query.Where(x => x.CreatedAtUtc >= from.Value);
        if (to.HasValue)
            query = query.Where(x => x.CreatedAtUtc <= to.Value);
        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(x => x.Type == type);
        var list = await query.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
        var dtos = list.Select(x => new OutboxOpsDto(
            x.Id,
            x.EventId,
            x.Type,
            x.CorrelationId,
            x.CreatedAtUtc,
            x.ProcessedAtUtc,
            x.AttemptCount,
            x.IsDeadLetter)).ToList();
        return Ok(dtos);
    }

    [HttpPost("outbox/{id:guid}/reprocess")]
    public async Task<IActionResult> Reprocess(Guid id, CancellationToken ct)
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
        catch (InvalidOperationException)
        {
            return BadRequest(new { error = "Only dead-letter messages can be reprocessed." });
        }
    }
}

public record PaymentOpsDto(
    Guid Id,
    Guid? TenantId,
    Guid TripId,
    Guid PayerId,
    decimal Amount,
    string Currency,
    string Status,
    string? ExternalPaymentId,
    DateTime CreatedAtUtc);

public record OutboxOpsDto(
    Guid Id,
    Guid EventId,
    string Type,
    string? CorrelationId,
    DateTime CreatedAtUtc,
    DateTime? ProcessedAtUtc,
    int AttemptCount,
    bool IsDeadLetter);
