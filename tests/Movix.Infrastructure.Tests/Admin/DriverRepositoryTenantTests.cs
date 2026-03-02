using Microsoft.EntityFrameworkCore;
using Moq;
using Movix.Application.Common.Interfaces;
using Movix.Application.Drivers;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Movix.Infrastructure.Persistence;
using Movix.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Movix.Infrastructure.Tests.Admin;

public class DriverRepositoryTenantTests
{
    private static readonly Guid TenantA = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("20000000-0000-0000-0000-000000000002");

    private static MovixDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<MovixDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new MovixDbContext(options);
    }

    private static async Task SeedTwoTenantsDriversAsync(MovixDbContext db)
    {
        if (await db.Tenants.AnyAsync())
            return;
        var now = DateTime.UtcNow;
        db.Tenants.Add(new Tenant { Id = TenantA, Name = "TenantA", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = new byte[] { 1 } });
        db.Tenants.Add(new Tenant { Id = TenantB, Name = "TenantB", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = new byte[] { 1 } });
        await db.SaveChangesAsync();

        var userA1 = Guid.NewGuid();
        var userA2 = Guid.NewGuid();
        var userB1 = Guid.NewGuid();
        db.Users.Add(new User { Id = userA1, TenantId = TenantA, Email = "a1@t.com", PasswordHash = "x", Role = Role.Driver, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = new byte[] { 1 } });
        db.Users.Add(new User { Id = userA2, TenantId = TenantA, Email = "a2@t.com", PasswordHash = "x", Role = Role.Driver, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = new byte[] { 1 } });
        db.Users.Add(new User { Id = userB1, TenantId = TenantB, Email = "b1@t.com", PasswordHash = "x", Role = Role.Driver, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = new byte[] { 1 } });
        await db.SaveChangesAsync();

        db.Drivers.Add(new Driver { Id = Guid.NewGuid(), UserId = userA1, TenantId = TenantA, Status = DriverStatus.Online, CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = new byte[] { 1 } });
        db.Drivers.Add(new Driver { Id = Guid.NewGuid(), UserId = userA2, TenantId = TenantA, Status = DriverStatus.Online, CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = new byte[] { 1 } });
        db.Drivers.Add(new Driver { Id = Guid.NewGuid(), UserId = userB1, TenantId = TenantB, Status = DriverStatus.Online, CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = new byte[] { 1 } });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AdminDrivers_WhenTenantContextSet_ReturnsOnlyTenantDrivers()
    {
        var dbName = "admin_drivers_tenant_" + Guid.NewGuid().ToString("N");
        await using var db = CreateInMemoryContext(dbName);
        await SeedTwoTenantsDriversAsync(db);

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.IsSuperAdmin).Returns(false);
        tenantContext.Setup(t => t.TenantId).Returns(TenantA);

        var repo = new DriverRepository(db, tenantContext.Object);
        var (drivers, total) = await repo.GetPagedAsync(1, 20, default);

        Assert.Equal(2, total);
        Assert.All(drivers, d => Assert.Equal(TenantA, d.TenantId));
    }
}
