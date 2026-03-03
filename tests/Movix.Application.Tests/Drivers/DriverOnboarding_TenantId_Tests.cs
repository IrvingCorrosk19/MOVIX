using Movix.Application.Common.Interfaces;
using Movix.Application.Drivers;
using Movix.Application.Drivers.Commands.DriverOnboarding;
using Movix.Domain.Entities;
using Moq;
using Xunit;

namespace Movix.Application.Tests.Drivers;

/// <summary>
/// BUG-001: Driver onboarding must set Driver.TenantId from the authenticated user's tenant (TenantContext).
/// Otherwise the Driver insert fails with FK violation because TenantId is required and was default/empty.
/// </summary>
public class DriverOnboarding_TenantId_Tests
{
    [Fact]
    public async Task With_authenticated_user_and_tenant_id_onboarding_creates_driver_with_TenantId_set()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        Driver? capturedDriver = null;
        var driverRepo = new Mock<IDriverRepository>();
        driverRepo.Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync((Driver?)null);
        driverRepo.Setup(r => r.AddAsync(It.IsAny<Driver>(), It.IsAny<CancellationToken>()))
            .Callback<Driver, CancellationToken>((d, _) => capturedDriver = d)
            .Returns(Task.CompletedTask);

        var availabilityRepo = new Mock<IDriverAvailabilityRepository>();
        availabilityRepo.Setup(r => r.AddAsync(It.IsAny<DriverAvailability>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.UserId).Returns(userId);

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(c => c.TenantId).Returns(tenantId);

        var dateTime = new Mock<IDateTimeService>();
        dateTime.Setup(s => s.UtcNow).Returns(now);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new DriverOnboardingCommandHandler(
            driverRepo.Object,
            availabilityRepo.Object,
            currentUser.Object,
            tenantContext.Object,
            dateTime.Object,
            uow.Object);

        var result = await handler.Handle(
            new DriverOnboardingCommand(null, "ABC123", "Model", "Red"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(capturedDriver);
        Assert.Equal(tenantId, capturedDriver.TenantId);
        Assert.Equal(userId, capturedDriver.UserId);
        Assert.Single(capturedDriver.Vehicles);
        Assert.Equal("ABC123", capturedDriver.Vehicles.First().Plate);
        driverRepo.Verify(r => r.AddAsync(It.IsAny<Driver>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task When_tenant_id_is_null_returns_TENANT_REQUIRED()
    {
        var userId = Guid.NewGuid();

        var driverRepo = new Mock<IDriverRepository>();
        driverRepo.Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync((Driver?)null);

        var availabilityRepo = new Mock<IDriverAvailabilityRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.UserId).Returns(userId);

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(c => c.TenantId).Returns((Guid?)null);

        var dateTime = new Mock<IDateTimeService>();
        var uow = new Mock<IUnitOfWork>();

        var handler = new DriverOnboardingCommandHandler(
            driverRepo.Object,
            availabilityRepo.Object,
            currentUser.Object,
            tenantContext.Object,
            dateTime.Object,
            uow.Object);

        var result = await handler.Handle(
            new DriverOnboardingCommand(null, "ABC123", null, null),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("TENANT_REQUIRED", result.ErrorCode);
        driverRepo.Verify(r => r.AddAsync(It.IsAny<Driver>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
