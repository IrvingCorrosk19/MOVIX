using Microsoft.Extensions.Options;
using Moq;
using Movix.Application.Common.Interfaces;
using Movix.Application.Outbox;
using Movix.Application.Payments;
using Movix.Application.Payments.Commands.CreatePayment;
using Movix.Application.Trips;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Movix.Infrastructure.Payments;
using Xunit;

namespace Movix.Infrastructure.Tests.Payments;

public class CreatePaymentCommandHandlerTests
{
    private readonly Mock<IPaymentRepository> _paymentRepo = new();
    private readonly Mock<ITripRepository> _tripRepo = new();
    private readonly Mock<IIdempotencyService> _idempotency = new();
    private readonly Mock<IOutboxMessageRepository> _outboxRepo = new();
    private readonly Mock<IPaymentGateway> _gateway = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IDateTimeService> _dateTime = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditService> _audit = new();

    private CreatePaymentCommandHandler CreateHandler() =>
        new(_paymentRepo.Object, _tripRepo.Object, _idempotency.Object, _outboxRepo.Object, _gateway.Object, _currentUser.Object, _tenantContext.Object, _dateTime.Object, _uow.Object, _audit.Object);

    private static Trip MakeTrip(Guid passengerId, TripStatus status, Guid? tenantId = null) => new()
    {
        Id = Guid.NewGuid(),
        PassengerId = passengerId,
        Status = status,
        TenantId = tenantId,
        IdempotencyKey = "ik",
        Currency = "USD",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        RowVersion = new byte[] { 1 }
    };

    [Fact]
    public async Task Handle_WhenTripDoesNotExist_ReturnsFailure()
    {
        var tripId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _currentUser.Setup(c => c.UserId).Returns(userId);
        _idempotency.Setup(i => i.GetResponseAsync(It.IsAny<string>(), default)).ReturnsAsync((string?)null);
        _tripRepo.Setup(r => r.GetByIdAsync(tripId, default)).ReturnsAsync((Trip?)null);

        var result = await CreateHandler().Handle(
            new CreatePaymentCommand("key", tripId, 10m, "USD"), default);

        Assert.False(result.Succeeded);
        Assert.Equal("TRIP_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task Handle_WhenTripNotCompleted_ReturnsFailure()
    {
        var tripId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var trip = MakeTrip(userId, TripStatus.InProgress);
        trip.Id = tripId;
        _currentUser.Setup(c => c.UserId).Returns(userId);
        _idempotency.Setup(i => i.GetResponseAsync(It.IsAny<string>(), default)).ReturnsAsync((string?)null);
        _tripRepo.Setup(r => r.GetByIdAsync(tripId, default)).ReturnsAsync(trip);

        var result = await CreateHandler().Handle(
            new CreatePaymentCommand("key", tripId, 10m, "USD"), default);

        Assert.False(result.Succeeded);
        Assert.Equal("TRIP_NOT_COMPLETED", result.ErrorCode);
    }

    [Fact]
    public async Task Handle_WhenUserIsNotPassenger_ReturnsFailure()
    {
        var tripId = Guid.NewGuid();
        var passengerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var trip = MakeTrip(passengerId, TripStatus.Completed);
        trip.Id = tripId;
        _currentUser.Setup(c => c.UserId).Returns(otherUserId);
        _idempotency.Setup(i => i.GetResponseAsync(It.IsAny<string>(), default)).ReturnsAsync((string?)null);
        _tripRepo.Setup(r => r.GetByIdAsync(tripId, default)).ReturnsAsync(trip);

        var result = await CreateHandler().Handle(
            new CreatePaymentCommand("key", tripId, 10m, "USD"), default);

        Assert.False(result.Succeeded);
        Assert.Equal("UNAUTHORIZED_PAYMENT", result.ErrorCode);
    }

    [Fact]
    public async Task Handle_WhenValid_ReturnsSuccess()
    {
        var tripId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var trip = MakeTrip(userId, TripStatus.Completed);
        trip.Id = tripId;
        _currentUser.Setup(c => c.UserId).Returns(userId);
        _idempotency.Setup(i => i.GetResponseAsync(It.IsAny<string>(), default)).ReturnsAsync((string?)null);
        _tripRepo.Setup(r => r.GetByIdAsync(tripId, default)).ReturnsAsync(trip);
        _dateTime.Setup(d => d.UtcNow).Returns(DateTime.UtcNow);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        _gateway.Setup(g => g.CreatePaymentIntentAsync(It.IsAny<CreatePaymentIntentRequest>(), default))
            .ReturnsAsync(new CreatePaymentIntentResult("pi_fake", "secret_fake"));

        var result = await CreateHandler().Handle(
            new CreatePaymentCommand("key", tripId, 10m, "USD"), default);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(tripId, result.Data!.TripId);
        Assert.Equal(10m, result.Data.Amount);
        Assert.Equal("Pending", result.Data.Status);
        Assert.Equal("secret_fake", result.Data.ClientSecret);

        _outboxRepo.Verify(
            r => r.AddAsync(It.Is<OutboxMessage>(m => m.Type == "PaymentCreated" && m.Payload.Contains(tripId.ToString())),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenValid_SetsExternalPaymentId_AndReturnsClientSecret()
    {
        Payment? captured = null;
        _paymentRepo.Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .Callback<Payment, CancellationToken>((p, _) => captured = p)
            .Returns(Task.CompletedTask);
        var tripId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var trip = MakeTrip(userId, TripStatus.Completed);
        trip.Id = tripId;
        _currentUser.Setup(c => c.UserId).Returns(userId);
        _idempotency.Setup(i => i.GetResponseAsync(It.IsAny<string>(), default)).ReturnsAsync((string?)null);
        _tripRepo.Setup(r => r.GetByIdAsync(tripId, default)).ReturnsAsync(trip);
        _dateTime.Setup(d => d.UtcNow).Returns(DateTime.UtcNow);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        _gateway.Setup(g => g.CreatePaymentIntentAsync(It.IsAny<CreatePaymentIntentRequest>(), default))
            .ReturnsAsync(new CreatePaymentIntentResult("pi_ext_123", "cs_secret_456"));

        var result = await CreateHandler().Handle(
            new CreatePaymentCommand("key", tripId, 10m, "USD"), default);

        Assert.True(result.Succeeded);
        Assert.NotNull(captured);
        Assert.Equal("pi_ext_123", captured.ExternalPaymentId);
        Assert.Equal("cs_secret_456", result.Data!.ClientSecret);
    }

    [Fact]
    public async Task Handle_WhenGatewayThrows_ReturnsPaymentGatewayError()
    {
        var tripId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var trip = MakeTrip(userId, TripStatus.Completed);
        trip.Id = tripId;
        _currentUser.Setup(c => c.UserId).Returns(userId);
        _idempotency.Setup(i => i.GetResponseAsync(It.IsAny<string>(), default)).ReturnsAsync((string?)null);
        _tripRepo.Setup(r => r.GetByIdAsync(tripId, default)).ReturnsAsync(trip);
        _dateTime.Setup(d => d.UtcNow).Returns(DateTime.UtcNow);
        _gateway.Setup(g => g.CreatePaymentIntentAsync(It.IsAny<CreatePaymentIntentRequest>(), default))
            .ThrowsAsync(new InvalidOperationException("Stripe error"));

        var result = await CreateHandler().Handle(
            new CreatePaymentCommand("key", tripId, 10m, "USD"), default);

        Assert.False(result.Succeeded);
        Assert.Equal("PAYMENT_GATEWAY_ERROR", result.ErrorCode);
    }

    [Fact]
    public async Task CreatePayment_WhenSimulationMode_ReturnsSimulatedClientSecret()
    {
        var simGateway = new SimulationPaymentGateway(Options.Create(new PaymentsOptions { Mode = "Simulation" }));
        var tripId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var trip = MakeTrip(userId, TripStatus.Completed);
        trip.Id = tripId;
        Payment? captured = null;
        _paymentRepo.Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .Callback<Payment, CancellationToken>((p, _) => captured = p)
            .Returns(Task.CompletedTask);
        _currentUser.Setup(c => c.UserId).Returns(userId);
        _idempotency.Setup(i => i.GetResponseAsync(It.IsAny<string>(), default)).ReturnsAsync((string?)null);
        _tripRepo.Setup(r => r.GetByIdAsync(tripId, default)).ReturnsAsync(trip);
        _dateTime.Setup(d => d.UtcNow).Returns(DateTime.UtcNow);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        _tenantContext.Setup(t => t.IsSuperAdmin).Returns(false);
        _tenantContext.Setup(t => t.TenantId).Returns((Guid?)null);
        var handler = new CreatePaymentCommandHandler(
            _paymentRepo.Object, _tripRepo.Object, _idempotency.Object, _outboxRepo.Object,
            simGateway, _currentUser.Object, _tenantContext.Object, _dateTime.Object, _uow.Object, _audit.Object);
        var result = await handler.Handle(new CreatePaymentCommand("key", tripId, 10m, "USD"), default);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.NotNull(result.Data!.ClientSecret);
        Assert.True(result.Data.ClientSecret.StartsWith("SIM_SECRET_"), "Expected ClientSecret to start with SIM_SECRET_");
        Assert.NotNull(captured);
        Assert.NotNull(captured!.ExternalPaymentId);
        Assert.True(captured.ExternalPaymentId.StartsWith("SIM_"), "Expected ExternalPaymentId to start with SIM_");
    }

    [Fact]
    public async Task CreatePayment_WhenTripFromOtherTenant_Returns403()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var trip = MakeTrip(userId, TripStatus.Completed, tenantB);
        trip.Id = tripId;
        _currentUser.Setup(c => c.UserId).Returns(userId);
        _tenantContext.Setup(t => t.IsSuperAdmin).Returns(false);
        _tenantContext.Setup(t => t.TenantId).Returns(tenantA);
        _idempotency.Setup(i => i.GetResponseAsync(It.IsAny<string>(), default)).ReturnsAsync((string?)null);
        _tripRepo.Setup(r => r.GetByIdAsync(tripId, default)).ReturnsAsync(trip);

        var result = await CreateHandler().Handle(
            new CreatePaymentCommand("key", tripId, 10m, "USD"), default);

        Assert.False(result.Succeeded);
        Assert.Equal("FORBIDDEN", result.ErrorCode);
    }
}
