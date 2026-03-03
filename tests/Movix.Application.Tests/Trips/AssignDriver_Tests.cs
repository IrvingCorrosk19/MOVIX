using Movix.Application.Common.Interfaces;
using Movix.Application.Drivers;
using Movix.Application.Outbox;
using Movix.Application.Trips;
using Movix.Application.Trips.Commands.AssignDriver;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Moq;
using Xunit;

namespace Movix.Application.Tests.Trips;

/// <summary>
/// Assign Driver: success updates trip to Accepted and adds TripStatusHistory;
/// ConcurrencyException must return CONFLICT (409), not NO_DRIVERS_AVAILABLE.
/// </summary>
public class AssignDriver_Tests
{
    [Fact]
    public async Task Assign_driver_success_changes_trip_to_Accepted_and_calls_AddStatusHistoryAsync()
    {
        var tripId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var trip = new Trip
        {
            Id = tripId,
            IdempotencyKey = "key",
            PassengerId = Guid.NewGuid(),
            TenantId = tenantId,
            Status = TripStatus.Requested,
            PickupAddress = "A",
            DropoffAddress = "B",
            Currency = "USD",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[] { 1 },
            StatusHistory = new List<TripStatusHistory>()
        };

        var availability = new DriverAvailability
        {
            DriverId = driverId,
            IsOnline = true,
            CurrentTripId = null,
            UpdatedAtUtc = now,
            RowVersion = new byte[] { 1 },
            Driver = new Driver { Id = driverId, TenantId = tenantId }
        };

        var tripRepo = new Mock<ITripRepository>();
        tripRepo.Setup(r => r.GetByIdAsync(tripId, It.IsAny<CancellationToken>())).ReturnsAsync(trip);
        tripRepo.Setup(r => r.AddStatusHistoryAsync(It.IsAny<TripStatusHistory>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var availabilityRepo = new Mock<IDriverAvailabilityRepository>();
        availabilityRepo.Setup(r => r.GetFirstAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(availability);

        var outboxRepo = new Mock<IOutboxMessageRepository>();
        outboxRepo.Setup(r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.UserId).Returns(Guid.NewGuid());
        currentUser.Setup(s => s.Role).Returns(Role.Admin);

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(c => c.TenantId).Returns(tenantId);
        tenantContext.Setup(c => c.IsSuperAdmin).Returns(false);

        var dateTime = new Mock<IDateTimeService>();
        dateTime.Setup(s => s.UtcNow).Returns(now);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = new AssignDriverCommandHandler(
            tripRepo.Object,
            availabilityRepo.Object,
            outboxRepo.Object,
            currentUser.Object,
            tenantContext.Object,
            dateTime.Object,
            uow.Object,
            audit.Object);

        var result = await handler.Handle(new AssignDriverCommand(tripId), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(TripStatus.Accepted, trip.Status);
        Assert.Equal(driverId, trip.DriverId);
        tripRepo.Verify(r => r.AddStatusHistoryAsync(It.Is<TripStatusHistory>(h =>
            h.TripId == tripId && h.FromStatus == TripStatus.Requested && h.ToStatus == TripStatus.Accepted),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task When_SaveChanges_throws_ConcurrencyException_returns_CONFLICT_not_NO_DRIVERS_AVAILABLE()
    {
        var tripId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var trip = new Trip
        {
            Id = tripId,
            IdempotencyKey = "key",
            PassengerId = Guid.NewGuid(),
            TenantId = tenantId,
            Status = TripStatus.Requested,
            PickupAddress = "A",
            DropoffAddress = "B",
            Currency = "USD",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[] { 1 },
            StatusHistory = new List<TripStatusHistory>()
        };

        var availability = new DriverAvailability
        {
            DriverId = driverId,
            IsOnline = true,
            CurrentTripId = null,
            UpdatedAtUtc = now,
            RowVersion = new byte[] { 1 },
            Driver = new Driver { Id = driverId, TenantId = tenantId }
        };

        var tripRepo = new Mock<ITripRepository>();
        tripRepo.Setup(r => r.GetByIdAsync(tripId, It.IsAny<CancellationToken>())).ReturnsAsync(trip);
        tripRepo.Setup(r => r.AddStatusHistoryAsync(It.IsAny<TripStatusHistory>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var availabilityRepo = new Mock<IDriverAvailabilityRepository>();
        availabilityRepo.Setup(r => r.GetFirstAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(availability);

        var outboxRepo = new Mock<IOutboxMessageRepository>();
        outboxRepo.Setup(r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.UserId).Returns(Guid.NewGuid());
        currentUser.Setup(s => s.Role).Returns(Role.Admin);

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(c => c.TenantId).Returns(tenantId);
        tenantContext.Setup(c => c.IsSuperAdmin).Returns(false);

        var dateTime = new Mock<IDateTimeService>();
        dateTime.Setup(s => s.UtcNow).Returns(now);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Movix.Application.Common.Exceptions.ConcurrencyException());

        var audit = new Mock<IAuditService>();

        var handler = new AssignDriverCommandHandler(
            tripRepo.Object,
            availabilityRepo.Object,
            outboxRepo.Object,
            currentUser.Object,
            tenantContext.Object,
            dateTime.Object,
            uow.Object,
            audit.Object);

        var result = await handler.Handle(new AssignDriverCommand(tripId), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("CONFLICT", result.ErrorCode);
        Assert.NotEqual("NO_DRIVERS_AVAILABLE", result.ErrorCode);
    }
}
