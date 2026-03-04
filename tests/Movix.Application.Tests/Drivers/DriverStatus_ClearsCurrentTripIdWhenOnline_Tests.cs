using Movix.Application.Common.Interfaces;
using Movix.Application.Drivers;
using Movix.Application.Drivers.Commands.DriverStatus;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Moq;
using Xunit;

namespace Movix.Application.Tests.Drivers;

/// <summary>
/// BUG-004: When a driver sets status to Online and an existing DriverAvailability record exists,
/// CurrentTripId must be cleared so the driver becomes available for assignment (defensive fix for stale state).
/// </summary>
public class DriverStatus_ClearsCurrentTripIdWhenOnline_Tests
{
    [Fact]
    public async Task When_setting_Online_on_existing_availability_clears_CurrentTripId()
    {
        var userId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var staleTripId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var driver = new Driver
        {
            Id = driverId,
            UserId = userId,
            Status = DriverStatus.Offline,
            TenantId = Guid.NewGuid(),
            RowVersion = new byte[] { 1 }
        };

        var availability = new DriverAvailability
        {
            DriverId = driverId,
            IsOnline = false,
            CurrentTripId = staleTripId,
            UpdatedAtUtc = now.AddHours(-1),
            RowVersion = new byte[] { 1 }
        };

        var driverRepo = new Mock<IDriverRepository>();
        driverRepo.Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(driver);

        var availabilityRepo = new Mock<IDriverAvailabilityRepository>();
        availabilityRepo.Setup(r => r.GetByDriverIdAsync(driverId, It.IsAny<CancellationToken>())).ReturnsAsync(availability);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.UserId).Returns(userId);

        var dateTime = new Mock<IDateTimeService>();
        dateTime.Setup(s => s.UtcNow).Returns(now);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new DriverStatusCommandHandler(
            driverRepo.Object,
            availabilityRepo.Object,
            currentUser.Object,
            dateTime.Object,
            uow.Object);

        var result = await handler.Handle(new DriverStatusCommand(DriverStatus.Online), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(availability.IsOnline);
        Assert.Null(availability.CurrentTripId);
        Assert.Equal(now, availability.UpdatedAtUtc);
    }
}
