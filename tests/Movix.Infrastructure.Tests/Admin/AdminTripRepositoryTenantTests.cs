using Microsoft.EntityFrameworkCore;
using Moq;
using Movix.Application.Admin;
using Movix.Application.Common.Interfaces;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Movix.Infrastructure.Persistence;
using Movix.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Movix.Infrastructure.Tests.Admin;

public class AdminTripRepositoryTenantTests
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

    private static async Task SeedTwoTenantsTripsAsync(MovixDbContext db)
    {
        if (await db.Tenants.AnyAsync())
            return;
        var now = DateTime.UtcNow;
        db.Tenants.Add(new Tenant { Id = TenantA, Name = "TenantA", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = new byte[] { 1 } });
        db.Tenants.Add(new Tenant { Id = TenantB, Name = "TenantB", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = new byte[] { 1 } });
        await db.SaveChangesAsync();

        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        db.Users.Add(new User { Id = userA, TenantId = TenantA, Email = "a@t.com", PasswordHash = "x", Role = Role.Passenger, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = new byte[] { 1 } });
        db.Users.Add(new User { Id = userB, TenantId = TenantB, Email = "b@t.com", PasswordHash = "x", Role = Role.Passenger, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = new byte[] { 1 } });
        await db.SaveChangesAsync();

        db.Trips.Add(new Trip { Id = Guid.NewGuid(), PassengerId = userA, TenantId = TenantA, Status = TripStatus.Requested, IdempotencyKey = "ik1", Currency = "USD", CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = new byte[] { 1 } });
        db.Trips.Add(new Trip { Id = Guid.NewGuid(), PassengerId = userA, TenantId = TenantA, Status = TripStatus.Completed, IdempotencyKey = "ik2", Currency = "USD", CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = new byte[] { 1 } });
        db.Trips.Add(new Trip { Id = Guid.NewGuid(), PassengerId = userB, TenantId = TenantB, Status = TripStatus.Requested, IdempotencyKey = "ik3", Currency = "USD", CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = new byte[] { 1 } });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AdminTrips_WhenTenantContextSet_ReturnsOnlyTenantTrips()
    {
        var dbName = "admin_trips_tenant_" + Guid.NewGuid().ToString("N");
        await using var db = CreateInMemoryContext(dbName);
        await SeedTwoTenantsTripsAsync(db);

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.IsSuperAdmin).Returns(false);
        tenantContext.Setup(t => t.TenantId).Returns(TenantA);

        var repo = new AdminTripRepository(db, tenantContext.Object);
        var (trips, total) = await repo.GetPagedAsync(1, 20, default);

        Assert.Equal(2, total);
        Assert.All(trips, t => Assert.Equal(TenantA, t.TenantId));
    }
}
