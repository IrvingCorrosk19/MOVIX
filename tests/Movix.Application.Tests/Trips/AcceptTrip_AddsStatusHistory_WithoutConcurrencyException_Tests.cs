using Movix.Application.Common.Interfaces;
using Movix.Application.Drivers;
using Movix.Application.Trips;
using Movix.Application.Trips.Commands.AcceptTrip;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Moq;
using Xunit;

namespace Movix.Application.Tests.Trips;

/// <summary>
/// BUG-002: When transitioning a trip to Accepted, a new TripStatusHistory must be inserted via
/// explicit DbSet.Add (repository.AddStatusHistoryAsync), not via trip.StatusHistory.Add().
/// Otherwise EF Core marks the new row as Modified and generates UPDATE; 0 rows affected causes
/// DbUpdateConcurrencyException and 409. This test verifies the handler uses the repository path
/// and completes without ConcurrencyException; total history count increase is asserted via the
/// single call to AddStatusHistoryAsync with the correct new status.
/// </summary>
public class AcceptTrip_AddsStatusHistory_WithoutConcurrencyException_Tests
{
    [Fact]
    public async Task Given_trip_in_Requested_When_AcceptTrip_Then_inserts_one_new_history_and_does_not_throw()
    {
        var tripId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
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
            StatusHistory = new List<TripStatusHistory>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    TripId = tripId,
                    FromStatus = TripStatus.Requested,
                    ToStatus = TripStatus.Requested,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                }
            }
        };

        var driver = new Driver
        {
            Id = driverId,
            UserId = driverUserId,
            TenantId = tenantId,
            Status = DriverStatus.Online,
            RowVersion = new byte[] { 1 },
            Vehicles = new List<Vehicle>
            {
                new()
                {
                    Id = vehicleId,
                    DriverId = driverId,
                    Plate = "X",
                    Model = "Y",
                    Color = "Z",
                    RowVersion = new byte[] { 1 }
                }
            }
        };

        TripStatusHistory? capturedHistory = null;
        var tripRepo = new Mock<ITripRepository>();
        tripRepo.Setup(r => r.GetByIdAsync(tripId, It.IsAny<CancellationToken>())).ReturnsAsync(trip);
        tripRepo.Setup(r => r.AddStatusHistoryAsync(It.IsAny<TripStatusHistory>(), It.IsAny<CancellationToken>()))
            .Callback<TripStatusHistory, CancellationToken>((h, _) => capturedHistory = h)
            .Returns(Task.CompletedTask);

        var driverRepo = new Mock<IDriverRepository>();
        driverRepo.Setup(r => r.GetByUserIdWithVehiclesAsync(driverUserId, It.IsAny<CancellationToken>())).ReturnsAsync(driver);

        var dateTime = new Mock<IDateTimeService>();
        dateTime.Setup(s => s.UtcNow).Returns(now);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.UserId).Returns(driverUserId);

        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new AcceptTripCommandHandler(
            tripRepo.Object,
            driverRepo.Object,
            currentUser.Object,
            dateTime.Object,
            uow.Object,
            audit.Object);

        var result = await handler.Handle(new AcceptTripCommand(tripId, vehicleId), CancellationToken.None);

        Assert.True(result.Succeeded);
        tripRepo.Verify(r => r.AddStatusHistoryAsync(It.IsAny<TripStatusHistory>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(capturedHistory);
        Assert.Equal(tripId, capturedHistory.TripId);
        Assert.Equal(TripStatus.Requested, capturedHistory.FromStatus);
        Assert.Equal(TripStatus.Accepted, capturedHistory.ToStatus);
        Assert.NotEqual(Guid.Empty, capturedHistory.Id);
        Assert.Equal(now, capturedHistory.CreatedAtUtc);
        Assert.Equal(now, capturedHistory.UpdatedAtUtc);
        // Total histories: original 1 + 1 new = 2; we verify the single new insert via AddStatusHistoryAsync.
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
