using Microsoft.Extensions.Logging;
using Moq;
using Movix.Application.Common.Interfaces;
using Movix.Application.Outbox;
using Movix.Application.Payments;
using Movix.Application.Payments.Commands.ProcessStripeWebhook;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Xunit;

namespace Movix.Application.Tests.Payments;

public class ProcessStripeWebhookCommandHandlerTests
{
    private readonly Mock<IPaymentRepository> _paymentRepo = new();
    private readonly Mock<IOutboxMessageRepository> _outboxRepo = new();
    private readonly Mock<IDateTimeService> _dateTime = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ILogger<ProcessStripeWebhookCommandHandler>> _logger = new();

    private ProcessStripeWebhookCommandHandler CreateHandler() =>
        new(_paymentRepo.Object, _outboxRepo.Object, _dateTime.Object, _uow.Object, _logger.Object);

    [Fact]
    public async Task Handle_WhenPaymentNotFound_ReturnsPaymentFoundFalse()
    {
        _paymentRepo.Setup(r => r.GetByExternalPaymentIdAsync("pi_unknown", default)).ReturnsAsync((Payment?)null);
        var parseResult = new PaymentWebhookParseResult(true, "evt_1", "payment_intent.succeeded", "pi_unknown", null);

        var result = await CreateHandler().Handle(new ProcessStripeWebhookCommand(parseResult), default);

        Assert.True(result.Succeeded);
        Assert.False(result.Data!.PaymentFound);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPaymentIntentSucceeded_SetsCompleted_AndAddsOutbox()
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            PayerId = Guid.NewGuid(),
            ExternalPaymentId = "pi_123",
            Status = PaymentStatus.Pending,
            Amount = 10m,
            Currency = "USD",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        };
        _paymentRepo.Setup(r => r.GetByExternalPaymentIdAsync("pi_123", default)).ReturnsAsync(payment);
        _dateTime.Setup(d => d.UtcNow).Returns(DateTime.UtcNow);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        var parseResult = new PaymentWebhookParseResult(true, "evt_1", "payment_intent.succeeded", "pi_123", null);

        var result = await CreateHandler().Handle(new ProcessStripeWebhookCommand(parseResult), default);

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.PaymentFound);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        _outboxRepo.Verify(
            r => r.AddAsync(It.Is<OutboxMessage>(m => m.Type == "PaymentCompleted" && m.Payload.Contains(payment.Id.ToString())),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPaymentIntentFailed_SetsFailed_AndFailureReason()
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            PayerId = Guid.NewGuid(),
            ExternalPaymentId = "pi_456",
            Status = PaymentStatus.Pending,
            Amount = 10m,
            Currency = "USD",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        };
        _paymentRepo.Setup(r => r.GetByExternalPaymentIdAsync("pi_456", default)).ReturnsAsync(payment);
        _dateTime.Setup(d => d.UtcNow).Returns(DateTime.UtcNow);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        var parseResult = new PaymentWebhookParseResult(true, "evt_2", "payment_intent.payment_failed", "pi_456", "Card declined");

        var result = await CreateHandler().Handle(new ProcessStripeWebhookCommand(parseResult), default);

        Assert.True(result.Succeeded);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("Card declined", payment.FailureReason);
        _outboxRepo.Verify(
            r => r.AddAsync(It.Is<OutboxMessage>(m => m.Type == "PaymentFailed"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenChargeRefunded_SetsRefunded()
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            PayerId = Guid.NewGuid(),
            ExternalPaymentId = "pi_789",
            Status = PaymentStatus.Completed,
            Amount = 10m,
            Currency = "USD",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        };
        _paymentRepo.Setup(r => r.GetByExternalPaymentIdAsync("pi_789", default)).ReturnsAsync(payment);
        _dateTime.Setup(d => d.UtcNow).Returns(DateTime.UtcNow);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        var parseResult = new PaymentWebhookParseResult(true, "evt_3", "charge.refunded", "pi_789", null);

        var result = await CreateHandler().Handle(new ProcessStripeWebhookCommand(parseResult), default);

        Assert.True(result.Succeeded);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        _outboxRepo.Verify(
            r => r.AddAsync(It.Is<OutboxMessage>(m => m.Type == "PaymentRefunded"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenAlreadyCompleted_AndEventIsFailed_DoesNotDowngrade()
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            PayerId = Guid.NewGuid(),
            ExternalPaymentId = "pi_d1",
            Status = PaymentStatus.Completed,
            Amount = 10m,
            Currency = "USD",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        };
        _paymentRepo.Setup(r => r.GetByExternalPaymentIdAsync("pi_d1", default)).ReturnsAsync(payment);
        var parseResult = new PaymentWebhookParseResult(true, "evt_fail", "payment_intent.payment_failed", "pi_d1", "Card declined");

        var result = await CreateHandler().Handle(new ProcessStripeWebhookCommand(parseResult), default);

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.PaymentFound);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        _outboxRepo.Verify(r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAlreadyFailed_AndEventIsSucceeded_DoesNotUpgrade()
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            PayerId = Guid.NewGuid(),
            ExternalPaymentId = "pi_d2",
            Status = PaymentStatus.Failed,
            FailureReason = "Card declined",
            Amount = 10m,
            Currency = "USD",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        };
        _paymentRepo.Setup(r => r.GetByExternalPaymentIdAsync("pi_d2", default)).ReturnsAsync(payment);
        var parseResult = new PaymentWebhookParseResult(true, "evt_retry", "payment_intent.succeeded", "pi_d2", null);

        var result = await CreateHandler().Handle(new ProcessStripeWebhookCommand(parseResult), default);

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.PaymentFound);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        _outboxRepo.Verify(r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAlreadyRefunded_AndEventIsSucceeded_DoesNothing()
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            PayerId = Guid.NewGuid(),
            ExternalPaymentId = "pi_d3",
            Status = PaymentStatus.Refunded,
            Amount = 10m,
            Currency = "USD",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        };
        _paymentRepo.Setup(r => r.GetByExternalPaymentIdAsync("pi_d3", default)).ReturnsAsync(payment);
        var parseResult = new PaymentWebhookParseResult(true, "evt_late", "payment_intent.succeeded", "pi_d3", null);

        var result = await CreateHandler().Handle(new ProcessStripeWebhookCommand(parseResult), default);

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.PaymentFound);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        _outboxRepo.Verify(r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SimulateWebhook_ChangesPaymentStatusCorrectly()
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            PayerId = Guid.NewGuid(),
            ExternalPaymentId = "SIM_abc123def456",
            Status = PaymentStatus.Pending,
            Amount = 25m,
            Currency = "USD",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        };
        _paymentRepo.Setup(r => r.GetByExternalPaymentIdAsync("SIM_abc123def456", default)).ReturnsAsync(payment);
        _dateTime.Setup(d => d.UtcNow).Returns(DateTime.UtcNow);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        var parseResult = new PaymentWebhookParseResult(true, "evt_sim", "payment_intent.succeeded", "SIM_abc123def456", null);

        var result = await CreateHandler().Handle(new ProcessStripeWebhookCommand(parseResult), default);

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.PaymentFound);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        _outboxRepo.Verify(
            r => r.AddAsync(It.Is<OutboxMessage>(m => m.Type == "PaymentCompleted"), It.IsAny<CancellationToken>()),
            Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
