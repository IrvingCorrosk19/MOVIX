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
/// BUG-003: Transition endpoints (arrive/start/complete) must compare Trip.DriverId (Driver entity Id)
/// with the current user's Driver Id, not User.Id. A driver user has User.Id and an associated Driver with Driver.Id;
/// only when currentUserDriverId == trip.DriverId should the transition be allowed.
/// </summary>
public class TransitionTrip_Driver_ABAC_Tests
{
    [Fact]
    public async Task Driver_user_with_associated_Driver_can_transition_to_Arrive_when_trip_has_that_DriverId()
    {
        var tripId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var trip = new Trip
        {
            Id = tripId,
            IdempotencyKey = "key",
            PassengerId = Guid.NewGuid(),
            DriverId = driverId,
            TenantId = tenantId,
            Status = TripStatus.Accepted,
            PickupAddress = "A",
            DropoffAddress = "B",
            Currency = "USD",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[] { 1 },
            StatusHistory = new List<TripStatusHistory>()
        };

        var tripRepo = new Mock<ITripRepository>();
        tripRepo.Setup(r => r.GetByIdWithDriverAsync(tripId, It.IsAny<CancellationToken>())).ReturnsAsync(trip);
        tripRepo.Setup(r => r.AddStatusHistoryAsync(It.IsAny<TripStatusHistory>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var driverRepo = new Mock<IDriverRepository>();
        driverRepo.Setup(r => r.GetDriverIdByUserIdAsync(driverUserId, It.IsAny<CancellationToken>())).ReturnsAsync(driverId);

        var tariffRepo = new Mock<ITariffPlanRepository>();
        var fareCalc = new Mock<IFareCalculator>();
        var availabilityRepo = new Mock<IDriverAvailabilityRepository>();
        var outboxRepo = new Mock<IOutboxMessageRepository>();
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(c => c.TenantId).Returns(tenantId);

        var dateTime = new Mock<IDateTimeService>();
        dateTime.Setup(s => s.UtcNow).Returns(now);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.UserId).Returns(driverUserId);
        currentUser.Setup(s => s.Role).Returns(Role.Driver);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new TransitionTripCommandHandler(
            tripRepo.Object,
            driverRepo.Object,
            tariffRepo.Object,
            fareCalc.Object,
            availabilityRepo.Object,
            outboxRepo.Object,
            tenantContext.Object,
            currentUser.Object,
            dateTime.Object,
            uow.Object);

        var result = await handler.Handle(
            new TransitionTripCommand(tripId, TripStatus.DriverArrived),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        driverRepo.Verify(r => r.GetDriverIdByUserIdAsync(driverUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Driver_user_with_different_Driver_gets_403_Forbidden()
    {
        var tripId = Guid.NewGuid();
        var tripDriverId = Guid.NewGuid();
        var otherDriverUserId = Guid.NewGuid();
        var otherDriverId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var trip = new Trip
        {
            Id = tripId,
            IdempotencyKey = "key",
            PassengerId = Guid.NewGuid(),
            DriverId = tripDriverId,
            TenantId = tenantId,
            Status = TripStatus.Accepted,
            PickupAddress = "A",
            DropoffAddress = "B",
            Currency = "USD",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[] { 1 },
            StatusHistory = new List<TripStatusHistory>()
        };

        var tripRepo = new Mock<ITripRepository>();
        tripRepo.Setup(r => r.GetByIdWithDriverAsync(tripId, It.IsAny<CancellationToken>())).ReturnsAsync(trip);

        var driverRepo = new Mock<IDriverRepository>();
        driverRepo.Setup(r => r.GetDriverIdByUserIdAsync(otherDriverUserId, It.IsAny<CancellationToken>())).ReturnsAsync(otherDriverId);

        var tariffRepo = new Mock<ITariffPlanRepository>();
        var fareCalc = new Mock<IFareCalculator>();
        var availabilityRepo = new Mock<IDriverAvailabilityRepository>();
        var outboxRepo = new Mock<IOutboxMessageRepository>();
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(c => c.TenantId).Returns(tenantId);

        var dateTime = new Mock<IDateTimeService>();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.UserId).Returns(otherDriverUserId);
        currentUser.Setup(s => s.Role).Returns(Role.Driver);

        var uow = new Mock<IUnitOfWork>();

        var handler = new TransitionTripCommandHandler(
            tripRepo.Object,
            driverRepo.Object,
            tariffRepo.Object,
            fareCalc.Object,
            availabilityRepo.Object,
            outboxRepo.Object,
            tenantContext.Object,
            currentUser.Object,
            dateTime.Object,
            uow.Object);

        var result = await handler.Handle(
            new TransitionTripCommand(tripId, TripStatus.DriverArrived),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("FORBIDDEN", result.ErrorCode);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task User_without_associated_Driver_gets_403_DRIVER_NOT_FOUND_for_arrive()
    {
        var tripId = Guid.NewGuid();
        var tripDriverId = Guid.NewGuid();
        var userIdNoDriver = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var trip = new Trip
        {
            Id = tripId,
            IdempotencyKey = "key",
            PassengerId = Guid.NewGuid(),
            DriverId = tripDriverId,
            TenantId = tenantId,
            Status = TripStatus.Accepted,
            PickupAddress = "A",
            DropoffAddress = "B",
            Currency = "USD",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[] { 1 },
            StatusHistory = new List<TripStatusHistory>()
        };

        var tripRepo = new Mock<ITripRepository>();
        tripRepo.Setup(r => r.GetByIdWithDriverAsync(tripId, It.IsAny<CancellationToken>())).ReturnsAsync(trip);

        var driverRepo = new Mock<IDriverRepository>();
        driverRepo.Setup(r => r.GetDriverIdByUserIdAsync(userIdNoDriver, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        var tariffRepo = new Mock<ITariffPlanRepository>();
        var fareCalc = new Mock<IFareCalculator>();
        var availabilityRepo = new Mock<IDriverAvailabilityRepository>();
        var outboxRepo = new Mock<IOutboxMessageRepository>();
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(c => c.TenantId).Returns(tenantId);

        var dateTime = new Mock<IDateTimeService>();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.UserId).Returns(userIdNoDriver);
        currentUser.Setup(s => s.Role).Returns(Role.Driver);

        var uow = new Mock<IUnitOfWork>();

        var handler = new TransitionTripCommandHandler(
            tripRepo.Object,
            driverRepo.Object,
            tariffRepo.Object,
            fareCalc.Object,
            availabilityRepo.Object,
            outboxRepo.Object,
            tenantContext.Object,
            currentUser.Object,
            dateTime.Object,
            uow.Object);

        var result = await handler.Handle(
            new TransitionTripCommand(tripId, TripStatus.DriverArrived),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("DRIVER_NOT_FOUND", result.ErrorCode);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
