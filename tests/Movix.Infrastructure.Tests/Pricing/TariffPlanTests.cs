using Microsoft.EntityFrameworkCore;
using Moq;
using Movix.Application.Common.Interfaces;
using Movix.Application.Pricing.Commands.ActivateTariffPlan;
using Movix.Application.Pricing.Queries.GetActiveTariffPlan;
using Movix.Application.Pricing.Queries.QuoteFare;
using Movix.Domain.Entities;
using Movix.Infrastructure.Persistence;
using Movix.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Movix.Infrastructure.Tests.Pricing;

public class TariffPlanTests
{
    private static MovixDbContext CreateInMemory(string name)
    {
        var options = new DbContextOptionsBuilder<MovixDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new MovixDbContext(options);
    }

    [Fact]
    public async Task ActivateTariffPlan_ActivatesPlan_WithoutDeactivatingOthers()
    {
        var dbName = $"tariff_act_{Guid.NewGuid()}";
        await using var db = CreateInMemory(dbName);
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var plan1 = new TariffPlan
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Plan A",
            Currency = "USD",
            BaseFare = 5m,
            PricePerKm = 1m,
            PricePerMinute = 0.2m,
            MinimumFare = 10m,
            IsActive = true,
            Priority = 100,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var plan2 = new TariffPlan
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Plan B",
            Currency = "USD",
            BaseFare = 3m,
            PricePerKm = 1.5m,
            PricePerMinute = 0.3m,
            MinimumFare = 8m,
            IsActive = false,
            Priority = 50,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.TariffPlans.AddRange(plan1, plan2);
        await db.SaveChangesAsync();

        var repo = new TariffPlanRepository(db);
        var uow = new UnitOfWork(db);
        var audit = new Mock<IAuditService>();
        var handler = new ActivateTariffPlanCommandHandler(repo, uow, audit.Object);
        await handler.Handle(new ActivateTariffPlanCommand(tenantId, plan2.Id), default);

        var applicable = await repo.GetApplicableTariffAsync(tenantId, now);
        Assert.NotNull(applicable);
        Assert.Equal(plan2.Id, applicable.Id);
        Assert.True(applicable.IsActive);
    }

    [Fact]
    public async Task GetActiveTariffPlan_WhenNoneApplicable_ReturnsFailure()
    {
        var dbName = $"tariff_get_{Guid.NewGuid()}";
        await using var db = CreateInMemory(dbName);
        var repo = new TariffPlanRepository(db);
        var dateTime = new Movix.Infrastructure.Services.DateTimeService();
        var handler = new GetActiveTariffPlanQueryHandler(repo, dateTime);
        var result = await handler.Handle(new GetActiveTariffPlanQuery(Guid.NewGuid()), default);
        Assert.False(result.Succeeded);
        Assert.Equal("NO_APPLICABLE_TARIFF", result.ErrorCode);
    }

    [Fact]
    public async Task QuoteFare_UsesApplicableTariff_ReturnsCalculatedFare()
    {
        var dbName = $"tariff_quote_{Guid.NewGuid()}";
        await using var db = CreateInMemory(dbName);
        var tenantId = Guid.NewGuid();
        var plan = new TariffPlan
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Standard",
            Currency = "USD",
            BaseFare = 5m,
            PricePerKm = 2m,
            PricePerMinute = 0.5m,
            MinimumFare = 10m,
            IsActive = true,
            Priority = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.TariffPlans.Add(plan);
        await db.SaveChangesAsync();

        var tariffRepo = new TariffPlanRepository(db);
        var calculator = new Movix.Application.Pricing.FareCalculator();
        var dateTime = new Movix.Infrastructure.Services.DateTimeService();
        var handler = new QuoteFareQueryHandler(tariffRepo, calculator, dateTime);
        var result = await handler.Handle(new QuoteFareQuery(tenantId, 10m, 20m), default);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(plan.Id, result.Data!.TariffPlanId);
        Assert.Equal("USD", result.Data.Currency);
        Assert.Equal(35.00m, result.Data.FareAmount); // 5 + 2*10 + 0.5*20 = 5+20+10 = 35
    }

    [Fact]
    public async Task QuoteFare_WithTenantId_UsesOnlyThatTenantsActiveTariff()
    {
        var dbName = $"tariff_tenant_{Guid.NewGuid()}";
        await using var db = CreateInMemory(dbName);
        var tenantA = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Tenant A",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        };
        var tenantB = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Tenant B",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        };
        db.Tenants.AddRange(tenantA, tenantB);
        var planA = new TariffPlan
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA.Id,
            Name = "Plan A",
            Currency = "USD",
            BaseFare = 10m,
            PricePerKm = 1m,
            PricePerMinute = 0.2m,
            MinimumFare = 15m,
            IsActive = true,
            Priority = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var planB = new TariffPlan
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB.Id,
            Name = "Plan B",
            Currency = "USD",
            BaseFare = 5m,
            PricePerKm = 2m,
            PricePerMinute = 0.5m,
            MinimumFare = 10m,
            IsActive = true,
            Priority = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.TariffPlans.AddRange(planA, planB);
        await db.SaveChangesAsync();

        var tariffRepo = new TariffPlanRepository(db);
        var calculator = new Movix.Application.Pricing.FareCalculator();
        var dateTime = new Movix.Infrastructure.Services.DateTimeService();
        var handler = new QuoteFareQueryHandler(tariffRepo, calculator, dateTime);

        var resultA = await handler.Handle(new QuoteFareQuery(tenantA.Id, 10m, 20m), default);
        var resultB = await handler.Handle(new QuoteFareQuery(tenantB.Id, 10m, 20m), default);

        Assert.True(resultA.Succeeded);
        Assert.True(resultB.Succeeded);
        Assert.Equal(planA.Id, resultA.Data!.TariffPlanId);
        Assert.Equal(planB.Id, resultB.Data!.TariffPlanId);
        Assert.Equal(24.00m, resultA.Data.FareAmount); // 10 + 1*10 + 0.2*20 = 10+10+4 = 24
        Assert.Equal(35.00m, resultB.Data.FareAmount); // 5 + 2*10 + 0.5*20 = 35
    }

    [Fact]
    public async Task GetApplicableTariff_WhenMultipleActive_ReturnsHighestPriority()
    {
        var dbName = $"tariff_priority_{Guid.NewGuid()}";
        await using var db = CreateInMemory(dbName);
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var lowPriority = new TariffPlan
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Low",
            Currency = "USD",
            BaseFare = 100m,
            PricePerKm = 1m,
            PricePerMinute = 1m,
            MinimumFare = 10m,
            IsActive = true,
            Priority = 200,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var highPriority = new TariffPlan
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "High",
            Currency = "USD",
            BaseFare = 5m,
            PricePerKm = 1m,
            PricePerMinute = 1m,
            MinimumFare = 10m,
            IsActive = true,
            Priority = 10,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.TariffPlans.AddRange(lowPriority, highPriority);
        await db.SaveChangesAsync();

        var repo = new TariffPlanRepository(db);
        var applicable = await repo.GetApplicableTariffAsync(tenantId, now);
        Assert.NotNull(applicable);
        Assert.Equal(highPriority.Id, applicable.Id);
        Assert.Equal(10, applicable.Priority);
    }

    [Fact]
    public async Task GetApplicableTariff_WhenOutsideWindow_IsIgnored()
    {
        var dbName = $"tariff_window_{Guid.NewGuid()}";
        await using var db = CreateInMemory(dbName);
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var futurePlan = new TariffPlan
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Future",
            Currency = "USD",
            BaseFare = 5m,
            PricePerKm = 1m,
            PricePerMinute = 1m,
            MinimumFare = 10m,
            IsActive = true,
            Priority = 100,
            EffectiveFromUtc = now.AddDays(1),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.TariffPlans.Add(futurePlan);
        await db.SaveChangesAsync();

        var repo = new TariffPlanRepository(db);
        var applicable = await repo.GetApplicableTariffAsync(tenantId, now);
        Assert.Null(applicable);
    }

    [Fact]
    public async Task TwoPlansSamePriorityOverlap_Returns_TARIFF_PRIORITY_CONFLICT()
    {
        var dbName = $"tariff_conflict_{Guid.NewGuid()}";
        await using var db = CreateInMemory(dbName);
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var plan1 = new TariffPlan
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Plan 1",
            Currency = "USD",
            BaseFare = 5m,
            PricePerKm = 1m,
            PricePerMinute = 1m,
            MinimumFare = 10m,
            IsActive = true,
            Priority = 100,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var plan2 = new TariffPlan
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Plan 2",
            Currency = "USD",
            BaseFare = 3m,
            PricePerKm = 1m,
            PricePerMinute = 1m,
            MinimumFare = 10m,
            IsActive = false,
            Priority = 100,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.TariffPlans.AddRange(plan1, plan2);
        await db.SaveChangesAsync();

        var repo = new TariffPlanRepository(db);
        var uow = new UnitOfWork(db);
        var audit = new Mock<IAuditService>();
        var handler = new ActivateTariffPlanCommandHandler(repo, uow, audit.Object);
        var result = await handler.Handle(new ActivateTariffPlanCommand(tenantId, plan2.Id), default);

        Assert.False(result.Succeeded);
        Assert.Equal("TARIFF_PRIORITY_CONFLICT", result.ErrorCode);
    }
}
