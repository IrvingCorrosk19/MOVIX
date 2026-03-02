using Microsoft.Extensions.Options;
using Movix.Application.Payments;

namespace Movix.Infrastructure.Payments;

public sealed class SimulationPaymentGateway : IPaymentGateway
{
    private readonly SimulationOptions _simulation;

    public SimulationPaymentGateway(IOptions<PaymentsOptions> options)
    {
        _simulation = options.Value.Simulation ?? new SimulationOptions();
    }

    public Task<CreatePaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest req, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        return Task.FromResult(new CreatePaymentIntentResult(
            "SIM_" + id.ToString("N"),
            "SIM_SECRET_" + id.ToString("N")));
    }

    public Task<PaymentWebhookParseResult> ParseAndValidateWebhookAsync(string json, string signatureHeader, CancellationToken ct)
    {
        var eventId = Guid.NewGuid().ToString();
        var eventType = (_simulation.DefaultResult ?? "success").Trim().Equals("failure", StringComparison.OrdinalIgnoreCase)
            ? "payment_intent.payment_failed"
            : "payment_intent.succeeded";
        string? externalPaymentId = null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("externalPaymentId", out var extId))
                externalPaymentId = extId.GetString();
            if (root.TryGetProperty("eventType", out var evType))
                eventType = evType.GetString() ?? eventType;
            else if (root.TryGetProperty("type", out var typeProp))
                eventType = typeProp.GetString() ?? eventType;
        }
        catch
        {
            // use defaults
        }
        if (string.IsNullOrWhiteSpace(externalPaymentId))
            externalPaymentId = "SIM_" + Guid.NewGuid().ToString("N");
        var failureReason = eventType == "payment_intent.payment_failed" ? "Simulated failure" : null;
        return Task.FromResult(new PaymentWebhookParseResult(true, eventId, eventType, externalPaymentId, failureReason));
    }
}
