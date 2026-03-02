namespace Movix.Application.Payments;

public record CreatePaymentIntentResult(string ExternalPaymentId, string ClientSecret);
