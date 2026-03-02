using Moq;
using Movix.Application.Common.Interfaces;
using Movix.Application.Drivers;
using Movix.Application.Outbox;
using Movix.Application.Pricing;
using Movix.Application.Trips;
using Movix.Application.Trips.Commands.TransitionTrip;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Xunit;

namespace Movix.Application.Tests.Trips;

public class TransitionTripCommandHandlerTenantTests
{
    [Fact]
    public async Task Handle_WhenTripHasNoTenantIdAndContextHasNoTenant_Returns_TENANT_REQUIRED()
    {
        var tripRepo = new Mock<ITripRepository>();
        var tariffRepo = new Mock<ITariffPlanRepository>();
        var fareCalc = new Mock<IFareCalculator>();
        var availRepo = new Mock<IDriverAvailabilityRepository>();
        var tenantContext = new Mock<ITenantContext>();
        var currentUser = new Mock<ICurrentUserService>();
        var dateTime = new Mock<IDateTimeService>();
        var uow = new Mock<IUnitOfWork>();

        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            PassengerId = Guid.NewGuid(),
            DriverId = Guid.NewGuid(),
            TenantId = null,
            Status = TripStatus.Accepted,
            IdempotencyKey = "k",
            Currency = "USD",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        };
        tripRepo.Setup(r => r.GetByIdWithDriverAsync(trip.Id, default)).ReturnsAsync(trip);
        tenantContext.Setup(c => c.TenantId).Returns((Guid?)null);
        currentUser.Setup(c => c.UserId).Returns(trip.DriverId);
        currentUser.Setup(c => c.Role).Returns(Role.Driver);

        var outboxRepo = new Mock<IOutboxMessageRepository>();
        var handler = new TransitionTripCommandHandler(
            tripRepo.Object, tariffRepo.Object, fareCalc.Object, availRepo.Object,
            outboxRepo.Object, tenantContext.Object, currentUser.Object, dateTime.Object, uow.Object);

        var result = await handler.Handle(new TransitionTripCommand(trip.Id, TripStatus.DriverArrived), default);

        Assert.False(result.Succeeded);
        Assert.Equal("TENANT_REQUIRED", result.ErrorCode);
    }

    [Fact]
    public async Task Handle_WhenTripHasNoTenantIdAndContextHasTenant_PersistsTenantIdOnTrip()
    {
        var tripRepo = new Mock<ITripRepository>();
        var tariffRepo = new Mock<ITariffPlanRepository>();
        var fareCalc = new Mock<IFareCalculator>();
        var availRepo = new Mock<IDriverAvailabilityRepository>();
        var tenantContext = new Mock<ITenantContext>();
        var currentUser = new Mock<ICurrentUserService>();
        var dateTime = new Mock<IDateTimeService>();
        var uow = new Mock<IUnitOfWork>();

        var tenantId = Guid.NewGuid();
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            PassengerId = Guid.NewGuid(),
            DriverId = Guid.NewGuid(),
            TenantId = null,
            Status = TripStatus.Accepted,
            IdempotencyKey = "k",
            Currency = "USD",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        };
        tripRepo.Setup(r => r.GetByIdWithDriverAsync(trip.Id, default)).ReturnsAsync(trip);
        tenantContext.Setup(c => c.TenantId).Returns(tenantId);
        currentUser.Setup(c => c.UserId).Returns(trip.DriverId);
        currentUser.Setup(c => c.Role).Returns(Role.Driver);
        dateTime.Setup(d => d.UtcNow).Returns(DateTime.UtcNow);

        var outboxRepo = new Mock<IOutboxMessageRepository>();
        var handler = new TransitionTripCommandHandler(
            tripRepo.Object, tariffRepo.Object, fareCalc.Object, availRepo.Object,
            outboxRepo.Object, tenantContext.Object, currentUser.Object, dateTime.Object, uow.Object);

        var result = await handler.Handle(new TransitionTripCommand(trip.Id, TripStatus.DriverArrived), default);

        Assert.True(result.Succeeded);
        Assert.Equal(tenantId, trip.TenantId);
    }
}
