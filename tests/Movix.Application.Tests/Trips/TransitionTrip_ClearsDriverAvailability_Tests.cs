using Movix.Application.Common.Interfaces;
using Movix.Application.Drivers;
using Movix.Application.Outbox;
using Movix.Application.Pricing;
using Movix.Application.Trips;
using Movix.Application.Trips.Commands.TransitionTrip;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Moq;
using Xunit;

namespace Movix.Application.Tests.Trips;

/// <summary>
/// BUG-004: When a trip transitions to Completed or Cancelled, DriverAvailability.CurrentTripId must be cleared
/// in the same transaction so the driver becomes available again.
/// </summary>
public class TransitionTrip_ClearsDriverAvailability_Tests
{
    [Fact]
    public async Task When_transitioning_to_Completed_clears_CurrentTripId_and_updates_UpdatedAtUtc()
    {
        var tripId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var trip = new Trip
        {
            Id = tripId,
            PassengerId = Guid.NewGuid(),
            DriverId = driverId,
            Status = TripStatus.InProgress,
            TenantId = tenantId,
            CreatedAtUtc = now.AddHours(-1),
            UpdatedAtUtc = now.AddMinutes(-5),
            RowVersion = new byte[] { 1 }
        };

        var availability = new DriverAvailability
        {
            DriverId = driverId,
            IsOnline = true,
            CurrentTripId = tripId,
            UpdatedAtUtc = now.AddMinutes(-10),
            RowVersion = new byte[] { 1 }
        };

        var tripRepo = new Mock<ITripRepository>();
        tripRepo.Setup(r => r.GetByIdWithDriverAsync(tripId, It.IsAny<CancellationToken>())).ReturnsAsync(trip);
        tripRepo.Setup(r => r.AddStatusHistoryAsync(It.IsAny<TripStatusHistory>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var driverRepo = new Mock<IDriverRepository>();
        driverRepo.Setup(r => r.GetDriverIdByUserIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(driverId);

        var tariffRepo = new Mock<ITariffPlanRepository>();
        tariffRepo.Setup(r => r.GetApplicableTariffAsync(tenantId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync((TariffPlan?)null);

        var availabilityRepo = new Mock<IDriverAvailabilityRepository>();
        availabilityRepo.Setup(r => r.GetByDriverIdAsync(driverId, It.IsAny<CancellationToken>())).ReturnsAsync(availability);

        var outboxRepo = new Mock<IOutboxMessageRepository>();
        outboxRepo.Setup(r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(c => c.TenantId).Returns(tenantId);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.UserId).Returns(userId);
        currentUser.Setup(s => s.Role).Returns(Role.Driver);

        var dateTime = new Mock<IDateTimeService>();
        dateTime.Setup(s => s.UtcNow).Returns(now);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new TransitionTripCommandHandler(
            tripRepo.Object,
            driverRepo.Object,
            tariffRepo.Object,
            Mock.Of<IFareCalculator>(),
            availabilityRepo.Object,
            outboxRepo.Object,
            tenantContext.Object,
            currentUser.Object,
            dateTime.Object,
            uow.Object);

        var result = await handler.Handle(
            new TransitionTripCommand(tripId, TripStatus.Completed, null, 5.0m, 18),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(availability.CurrentTripId);
        Assert.Equal(now, availability.UpdatedAtUtc);
    }

    [Fact]
    public async Task When_transitioning_to_Cancelled_clears_CurrentTripId_and_updates_UpdatedAtUtc()
    {
        var tripId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var trip = new Trip
        {
            Id = tripId,
            PassengerId = Guid.NewGuid(),
            DriverId = driverId,
            Status = TripStatus.Accepted,
            TenantId = tenantId,
            CreatedAtUtc = now.AddHours(-1),
            UpdatedAtUtc = now.AddMinutes(-5),
            RowVersion = new byte[] { 1 }
        };

        var availability = new DriverAvailability
        {
            DriverId = driverId,
            IsOnline = true,
            CurrentTripId = tripId,
            UpdatedAtUtc = now.AddMinutes(-10),
            RowVersion = new byte[] { 1 }
        };

        var tripRepo = new Mock<ITripRepository>();
        tripRepo.Setup(r => r.GetByIdWithDriverAsync(tripId, It.IsAny<CancellationToken>())).ReturnsAsync(trip);
        tripRepo.Setup(r => r.AddStatusHistoryAsync(It.IsAny<TripStatusHistory>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var driverRepo = new Mock<IDriverRepository>();
        driverRepo.Setup(r => r.GetDriverIdByUserIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(driverId);

        var availabilityRepo = new Mock<IDriverAvailabilityRepository>();
        availabilityRepo.Setup(r => r.GetByDriverIdAsync(driverId, It.IsAny<CancellationToken>())).ReturnsAsync(availability);

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(c => c.TenantId).Returns(tenantId);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.UserId).Returns(userId);
        currentUser.Setup(s => s.Role).Returns(Role.Driver);

        var dateTime = new Mock<IDateTimeService>();
        dateTime.Setup(s => s.UtcNow).Returns(now);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new TransitionTripCommandHandler(
            tripRepo.Object,
            driverRepo.Object,
            Mock.Of<ITariffPlanRepository>(),
            Mock.Of<IFareCalculator>(),
            availabilityRepo.Object,
            Mock.Of<IOutboxMessageRepository>(),
            tenantContext.Object,
            currentUser.Object,
            dateTime.Object,
            uow.Object);

        var result = await handler.Handle(
            new TransitionTripCommand(tripId, TripStatus.Cancelled, "Passenger cancelled"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(availability.CurrentTripId);
        Assert.Equal(now, availability.UpdatedAtUtc);
    }
}
