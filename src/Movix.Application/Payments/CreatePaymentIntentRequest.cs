namespace Movix.Application.Payments;

public record CreatePaymentIntentRequest(
    decimal Amount,
    string Currency,
    Guid PaymentId,
    Guid TripId,
    Guid PayerId,
    string? CorrelationId);
