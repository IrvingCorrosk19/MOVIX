using Microsoft.EntityFrameworkCore;
using Moq;
using Movix.Application.Common.Interfaces;
using Movix.Domain.Enums;
using Movix.Infrastructure.Persistence;
using Movix.Infrastructure.Services;
using Xunit;

namespace Movix.Infrastructure.Tests.Audit;

public class AuditServiceTests
{
    [Fact]
    public async Task AuditService_LogAsync_PersistsRecord()
    {
        var dbName = $"audit_{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<MovixDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        await using var db = new MovixDbContext(options);

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(c => c.UserId).Returns(userId);
        currentUser.Setup(c => c.Role).Returns(Role.Admin);
        var dateTime = new Mock<IDateTimeService>();
        dateTime.Setup(d => d.UtcNow).Returns(DateTime.UtcNow);

        var audit = new AuditService(db, tenant.Object, currentUser.Object, dateTime.Object);
        await audit.LogAsync("TestAction", "TestEntity", Guid.NewGuid(), new { key = "value" });

        var count = await db.AuditLogs.CountAsync();
        Assert.Equal(1, count);
        var log = await db.AuditLogs.FirstAsync();
        Assert.Equal(tenantId, log.TenantId);
        Assert.Equal(userId, log.UserId);
        Assert.Equal("Admin", log.Role);
        Assert.Equal("TestAction", log.Action);
        Assert.Equal("TestEntity", log.EntityType);
        Assert.NotNull(log.Metadata);
    }
}
