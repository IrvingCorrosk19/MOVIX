using MediatR;
using Movix.Application.Common.Models;
using Movix.Application.Payments;

namespace Movix.Application.Payments.Commands.ProcessStripeWebhook;

public record ProcessStripeWebhookCommand(PaymentWebhookParseResult ParseResult) : IRequest<Result<ProcessStripeWebhookResult>>;

public record ProcessStripeWebhookResult(bool PaymentFound);
