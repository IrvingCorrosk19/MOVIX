namespace Movix.Application.Payments;

public interface IPaymentGateway
{
    Task<CreatePaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest req, CancellationToken ct);
    Task<PaymentWebhookParseResult> ParseAndValidateWebhookAsync(string json, string signatureHeader, CancellationToken ct);
}
