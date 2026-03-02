using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Movix.Application.Payments;

namespace Movix.Infrastructure.Payments;

public sealed class StripePaymentGateway : IPaymentGateway
{
    private readonly StripeOptions _options;
    private readonly ILogger<StripePaymentGateway> _logger;

    public StripePaymentGateway(IOptions<StripeOptions> options, ILogger<StripePaymentGateway> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CreatePaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest req, CancellationToken ct)
    {
        Stripe.StripeConfiguration.ApiKey = _options.SecretKey;
        var service = new Stripe.PaymentIntentService();
        var createOptions = new Stripe.PaymentIntentCreateOptions
        {
            Amount = (long)Math.Round(req.Amount * 100),
            Currency = req.Currency.ToLowerInvariant(),
            AutomaticPaymentMethods = new Stripe.PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true },
            Metadata = new Dictionary<string, string>
            {
                ["paymentId"] = req.PaymentId.ToString(),
                ["tripId"] = req.TripId.ToString(),
                ["payerId"] = req.PayerId.ToString()
            }
        };
        if (!string.IsNullOrWhiteSpace(req.CorrelationId))
            createOptions.Metadata["correlationId"] = req.CorrelationId;

        var intent = await service.CreateAsync(createOptions, cancellationToken: ct);
        return new CreatePaymentIntentResult(intent.Id, intent.ClientSecret);
    }

    public Task<PaymentWebhookParseResult> ParseAndValidateWebhookAsync(string json, string signatureHeader, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            _logger.LogWarning("Stripe WebhookSecret not configured.");
            return Task.FromResult(new PaymentWebhookParseResult(false, null, null, null, null));
        }
        try
        {
            var stripeEvent = Stripe.EventUtility.ConstructEvent(json, signatureHeader, _options.WebhookSecret);
            string? externalPaymentId = null;
            string? failureReason = null;

            if (stripeEvent.Data?.Object is Stripe.PaymentIntent pi)
            {
                externalPaymentId = pi.Id;
                if (pi.LastPaymentError?.Message != null)
                    failureReason = pi.LastPaymentError.Message;
            }
            else if (stripeEvent.Data?.Object is Stripe.Charge charge)
            {
                externalPaymentId = charge.PaymentIntentId ?? charge.Id;
            }

            return Task.FromResult(new PaymentWebhookParseResult(
                true,
                stripeEvent.Id,
                stripeEvent.Type,
                externalPaymentId,
                failureReason));
        }
        catch (Stripe.StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook signature validation failed.");
            return Task.FromResult(new PaymentWebhookParseResult(false, null, null, null, null));
        }
    }
}
