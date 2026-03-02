using Moq;
using Movix.Application.Common.Interfaces;
using Movix.Application.Pricing;
using Movix.Application.Pricing.Commands.UpdateTariffPlan;
using Movix.Domain.Entities;
using Xunit;

namespace Movix.Application.Tests.Pricing;

public class UpdateTariffPlanCrossTenantTests
{
    [Fact]
    public async Task Handle_WhenPlanBelongsToOtherTenant_Returns_TARIFF_CROSS_TENANT()
    {
        var repo = new Mock<ITariffPlanRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var dateTime = new Mock<IDateTimeService>();
        var uow = new Mock<IUnitOfWork>();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var plan = new TariffPlan
        {
            Id = planId,
            TenantId = tenantB,
            Name = "Other",
            Currency = "USD",
            BaseFare = 1,
            PricePerKm = 1,
            PricePerMinute = 1,
            MinimumFare = 1,
            IsActive = false,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        repo.Setup(r => r.GetByIdAsync(planId, default)).ReturnsAsync(plan);
        dateTime.Setup(d => d.UtcNow).Returns(DateTime.UtcNow);

        var handler = new UpdateTariffPlanCommandHandler(repo.Object, currentUser.Object, dateTime.Object, uow.Object);
        var result = await handler.Handle(new UpdateTariffPlanCommand(
            tenantA, planId, "New", "USD", 2, 2, 2, 2, null), default);

        Assert.False(result.Succeeded);
        Assert.Equal("TARIFF_CROSS_TENANT", result.ErrorCode);
    }
}
