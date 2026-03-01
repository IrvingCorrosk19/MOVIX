using MediatR;
using Movix.Application.Common.Models;

namespace Movix.Application.Payments.Commands.CreatePayment;

public record CreatePaymentCommand(
    string IdempotencyKey,
    Guid TripId,
    decimal Amount,
    string Currency = "USD") : IRequest<Result<PaymentDto>>;

public record PaymentDto(
    Guid Id,
    Guid TripId,
    decimal Amount,
    string Currency,
    string Status,
    DateTime CreatedAtUtc);
