using Moq;
using Movix.Application.Common.Interfaces;
using Movix.Application.Drivers;
using Movix.Application.Trips;
using Movix.Application.Trips.Commands.AcceptTrip;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Xunit;

namespace Movix.Application.Tests.Trips;

public class AcceptTripCommandHandlerTests
{
    private static Trip MakeTrip(Guid? driverId, TripStatus status, Guid? tenantId = null) => new()
    {
        Id = Guid.NewGuid(),
        PassengerId = Guid.NewGuid(),
        DriverId = driverId,
        Status = status,
        TenantId = tenantId,
        IdempotencyKey = "ik",
        Currency = "USD",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        RowVersion = new byte[] { 1 }
    };

    [Fact]
    public async Task AcceptTrip_WhenDriverFromOtherTenant_Returns403()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var trip = MakeTrip(null, TripStatus.Requested, tenantA);
        var driverId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var driver = new Driver
        {
            Id = driverId,
            UserId = driverUserId,
            TenantId = tenantB,
            Status = DriverStatus.Online,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        };
        driver.Vehicles.Add(new Vehicle { Id = vehicleId, DriverId = driverId });

        var tripRepo = new Mock<ITripRepository>();
        var driverRepo = new Mock<IDriverRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var dateTime = new Mock<IDateTimeService>();
        var uow = new Mock<IUnitOfWork>();

        tripRepo.Setup(r => r.GetByIdAsync(trip.Id, default)).ReturnsAsync(trip);
        driverRepo.Setup(r => r.GetByUserIdWithVehiclesAsync(driverUserId, default)).ReturnsAsync(driver);
        currentUser.Setup(c => c.UserId).Returns(driverUserId);
        dateTime.Setup(d => d.UtcNow).Returns(DateTime.UtcNow);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var audit = new Mock<IAuditService>();
        var handler = new AcceptTripCommandHandler(
            tripRepo.Object, driverRepo.Object, currentUser.Object, dateTime.Object, uow.Object, audit.Object);

        var result = await handler.Handle(new AcceptTripCommand(trip.Id, vehicleId), default);

        Assert.False(result.Succeeded);
        Assert.Equal("FORBIDDEN", result.ErrorCode);
    }
}
