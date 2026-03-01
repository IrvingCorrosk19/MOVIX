using Moq;
using Movix.Application.Common.Interfaces;
using Movix.Application.Payments;
using Movix.Application.Payments.Commands.CreatePayment;
using Movix.Application.Trips;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Xunit;

namespace Movix.Infrastructure.Tests.Payments;

public class CreatePaymentCommandHandlerTests
{
    private readonly Mock<IPaymentRepository> _paymentRepo = new();
    private readonly Mock<ITripRepository> _tripRepo = new();
    private readonly Mock<IIdempotencyService> _idempotency = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<IDateTimeService> _dateTime = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private CreatePaymentCommandHandler CreateHandler() =>
        new(_paymentRepo.Object, _tripRepo.Object, _idempotency.Object, _currentUser.Object, _dateTime.Object, _uow.Object);

    private static Trip MakeTrip(Guid passengerId, TripStatus status) => new()
    {
        Id = Guid.NewGuid(),
        PassengerId = passengerId,
        Status = status,
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

        var result = await CreateHandler().Handle(
            new CreatePaymentCommand("key", tripId, 10m, "USD"), default);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(tripId, result.Data!.TripId);
        Assert.Equal(10m, result.Data.Amount);
        Assert.Equal("Pending", result.Data.Status);
    }
}
