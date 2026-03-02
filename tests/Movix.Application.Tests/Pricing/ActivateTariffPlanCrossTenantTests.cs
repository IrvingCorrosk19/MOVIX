using Moq;
using Movix.Application.Common.Interfaces;
using Movix.Application.Pricing;
using Movix.Application.Pricing.Commands.ActivateTariffPlan;
using Movix.Domain.Entities;
using Xunit;

namespace Movix.Application.Tests.Pricing;

public class ActivateTariffPlanCrossTenantTests
{
    [Fact]
    public async Task Handle_WhenPlanBelongsToOtherTenant_Returns_TARIFF_CROSS_TENANT()
    {
        var repo = new Mock<ITariffPlanRepository>();
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

        var handler = new ActivateTariffPlanCommandHandler(repo.Object, uow.Object);
        var result = await handler.Handle(new ActivateTariffPlanCommand(tenantA, planId), default);

        Assert.False(result.Succeeded);
        Assert.Equal("TARIFF_CROSS_TENANT", result.ErrorCode);
    }
}
