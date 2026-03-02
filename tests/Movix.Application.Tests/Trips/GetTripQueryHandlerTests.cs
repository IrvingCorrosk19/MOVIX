using Moq;
using Movix.Application.Common.Interfaces;
using Movix.Application.Trips;
using Movix.Application.Trips.Queries.GetTrip;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Xunit;

namespace Movix.Application.Tests.Trips;

public class GetTripQueryHandlerTests
{
    [Fact]
    public async Task GetTrip_AsAdminFromOtherTenant_Returns404()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var trip = new Trip
        {
            Id = tripId,
            PassengerId = Guid.NewGuid(),
            DriverId = null,
            Status = TripStatus.Requested,
            TenantId = tenantB,
            IdempotencyKey = "ik",
            Currency = "USD",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        };

        var tripRepo = new Mock<ITripRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var tenantContext = new Mock<ITenantContext>();

        tripRepo.Setup(r => r.GetByIdAsync(tripId, default)).ReturnsAsync(trip);
        currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        currentUser.Setup(c => c.Role).Returns(Role.Admin);
        tenantContext.Setup(t => t.IsSuperAdmin).Returns(false);
        tenantContext.Setup(t => t.TenantId).Returns(tenantA);

        var handler = new GetTripQueryHandler(tripRepo.Object, currentUser.Object, tenantContext.Object);

        var result = await handler.Handle(new GetTripQuery(tripId), default);

        Assert.False(result.Succeeded);
        Assert.Equal("TRIP_NOT_FOUND", result.ErrorCode);
    }
}
