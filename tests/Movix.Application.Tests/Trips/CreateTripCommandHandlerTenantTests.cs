using Moq;
using Movix.Application.Common.Interfaces;
using Movix.Application.Trips;
using Movix.Application.Trips.Commands.CreateTrip;
using Xunit;

namespace Movix.Application.Tests.Trips;

public class CreateTripCommandHandlerTenantTests
{
    [Fact]
    public async Task Handle_WhenTenantIdIsNull_Returns_TENANT_REQUIRED()
    {
        var tripRepo = new Mock<ITripRepository>();
        var idempotency = new Mock<IIdempotencyService>();
        var currentUser = new Mock<ICurrentUserService>();
        var tenantContext = new Mock<ITenantContext>();
        var dateTime = new Mock<IDateTimeService>();
        var uow = new Mock<IUnitOfWork>();

        currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        tenantContext.Setup(c => c.TenantId).Returns((Guid?)null);
        idempotency.Setup(i => i.GetResponseAsync(It.IsAny<string>(), default)).ReturnsAsync((string?)null);

        var handler = new CreateTripCommandHandler(
            tripRepo.Object, idempotency.Object, currentUser.Object,
            tenantContext.Object, dateTime.Object, uow.Object);

        var result = await handler.Handle(new CreateTripCommand(
            "key-1", 0, 0, 0, 0, null, null, null, "USD"), default);

        Assert.False(result.Succeeded);
        Assert.Equal("TENANT_REQUIRED", result.ErrorCode);
    }
}
