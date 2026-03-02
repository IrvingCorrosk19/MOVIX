using MediatR;
using Microsoft.Extensions.Logging;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;
using Movix.Application.Outbox;
using Movix.Application.Payments;
using Movix.Domain.Entities;
using Movix.Domain.Enums;

namespace Movix.Application.Payments.Commands.ProcessStripeWebhook;

public class ProcessStripeWebhookCommandHandler : IRequestHandler<ProcessStripeWebhookCommand, Result<ProcessStripeWebhookResult>>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IOutboxMessageRepository _outboxRepository;
    private readonly IDateTimeService _dateTime;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<ProcessStripeWebhookCommandHandler> _logger;

    public ProcessStripeWebhookCommandHandler(
        IPaymentRepository paymentRepository,
        IOutboxMessageRepository outboxRepository,
        IDateTimeService dateTime,
        IUnitOfWork uow,
        ILogger<ProcessStripeWebhookCommandHandler> logger)
    {
        _paymentRepository = paymentRepository;
        _outboxRepository = outboxRepository;
        _dateTime = dateTime;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result<ProcessStripeWebhookResult>> Handle(ProcessStripeWebhookCommand request, CancellationToken cancellationToken)
    {
        var pr = request.ParseResult;
        if (string.IsNullOrWhiteSpace(pr.ExternalPaymentId))
            return Result<ProcessStripeWebhookResult>.Success(new ProcessStripeWebhookResult(false));

        var payment = await _paymentRepository.GetByExternalPaymentIdAsync(pr.ExternalPaymentId, cancellationToken);
        if (payment == null)
            return Result<ProcessStripeWebhookResult>.Success(new ProcessStripeWebhookResult(false));

        if (payment.Status == PaymentStatus.Completed)
        {
            if (pr.EventType is "payment_intent.payment_failed" or "payment_intent.canceled")
            {
                _logger.LogWarning("Stripe webhook: rejected status downgrade Completed->Failed for PaymentId {PaymentId}, EventId {EventId}", payment.Id, pr.EventId);
                return Result<ProcessStripeWebhookResult>.Success(new ProcessStripeWebhookResult(true));
            }
            if (pr.EventType == "payment_intent.succeeded")
                return Result<ProcessStripeWebhookResult>.Success(new ProcessStripeWebhookResult(true));
            if (pr.EventType != "charge.refunded")
                return Result<ProcessStripeWebhookResult>.Success(new ProcessStripeWebhookResult(true));
        }
        else if (payment.Status == PaymentStatus.Failed)
        {
            if (pr.EventType == "payment_intent.succeeded")
            {
                _logger.LogWarning("Stripe webhook: rejected status change Failed->Completed for PaymentId {PaymentId}, EventId {EventId}", payment.Id, pr.EventId);
                return Result<ProcessStripeWebhookResult>.Success(new ProcessStripeWebhookResult(true));
            }
            if (pr.EventType is "payment_intent.payment_failed" or "payment_intent.canceled")
                return Result<ProcessStripeWebhookResult>.Success(new ProcessStripeWebhookResult(true));
        }
        else if (payment.Status == PaymentStatus.Refunded)
        {
            _logger.LogWarning("Stripe webhook: rejected status change on Refunded payment PaymentId {PaymentId}, EventType {EventType}, EventId {EventId}", payment.Id, pr.EventType, pr.EventId);
            return Result<ProcessStripeWebhookResult>.Success(new ProcessStripeWebhookResult(true));
        }

        var now = _dateTime.UtcNow;
        string outboxType;
        switch (pr.EventType)
        {
            case "payment_intent.succeeded":
                payment.Status = PaymentStatus.Completed;
                payment.FailureReason = null;
                payment.UpdatedAtUtc = now;
                outboxType = "PaymentCompleted";
                break;
            case "payment_intent.payment_failed":
                payment.Status = PaymentStatus.Failed;
                payment.FailureReason = pr.FailureReason ?? "Payment failed";
                payment.UpdatedAtUtc = now;
                outboxType = "PaymentFailed";
                break;
            case "payment_intent.canceled":
                payment.Status = PaymentStatus.Failed;
                payment.FailureReason = pr.FailureReason ?? "Canceled";
                payment.UpdatedAtUtc = now;
                outboxType = "PaymentFailed";
                break;
            case "charge.refunded":
                payment.Status = PaymentStatus.Refunded;
                payment.FailureReason = null;
                payment.UpdatedAtUtc = now;
                outboxType = "PaymentRefunded";
                break;
            default:
                return Result<ProcessStripeWebhookResult>.Success(new ProcessStripeWebhookResult(true));
        }

        var failureEscaped = payment.FailureReason == null ? "" : payment.FailureReason.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var payload = "{\"paymentId\":\"" + payment.Id + "\",\"tripId\":\"" + payment.TripId + "\",\"payerId\":\"" + payment.PayerId + "\",\"amount\":" + payment.Amount + ",\"currency\":\"" + payment.Currency + "\",\"externalPaymentId\":\"" + (payment.ExternalPaymentId ?? "") + "\",\"occurredAtUtc\":\"" + now.ToString("O") + "\""
            + (string.IsNullOrEmpty(failureEscaped) ? "" : ",\"failureReason\":\"" + failureEscaped + "\"") + "}";

        await _outboxRepository.AddAsync(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = outboxType,
            Payload = payload,
            CreatedAtUtc = now
        }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return Result<ProcessStripeWebhookResult>.Success(new ProcessStripeWebhookResult(true));
    }
}
