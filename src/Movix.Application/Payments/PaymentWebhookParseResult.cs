namespace Movix.Application.Payments;

public record PaymentWebhookParseResult(
    bool IsValid,
    string? EventId,
    string? EventType,
    string? ExternalPaymentId,
    string? FailureReason);
