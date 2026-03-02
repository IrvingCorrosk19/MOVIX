using Microsoft.EntityFrameworkCore;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Movix.Infrastructure.Auth;
using Movix.Infrastructure.Persistence;
using Xunit;

namespace Movix.Infrastructure.Tests.Auth;

public class RegisterTests
{
    // Shared tenant GUID used across all tests
    private static readonly Guid TestTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static MovixDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<MovixDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new MovixDbContext(options);
    }

    private static JwtSettings DefaultJwtSettings() => new()
    {
        SecretKey = "movix-test-secret-key-min-32-characters-ok",
        Issuer = "movix-test",
        Audience = "movix-test",
        AccessTokenExpirationMinutes = 15,
        RefreshTokenExpirationDays = 7
    };

    /// Seeds a Tenant entity so that RegisterAsync passes the tenant existence check.
    private static async Task SeedTenantAsync(MovixDbContext db)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == TestTenantId))
        {
            var now = DateTime.UtcNow;
            db.Tenants.Add(new Tenant
            {
                Id = TestTenantId,
                Name = "Test Tenant",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RowVersion = new byte[] { 1 }
            });
            await db.SaveChangesAsync();
        }
    }

    // F-1 — Registro exitoso: crea usuario Passenger + evento en outbox
    [Fact]
    public async Task Register_NewUser_CreatesUserAndOutbox()
    {
        var dbName = $"register_new_{Guid.NewGuid()}";
        await using var db = CreateInMemoryContext(dbName);
        await SeedTenantAsync(db);

        var service = new AuthService(db, DefaultJwtSettings());
        var result = await service.RegisterAsync("alice@example.com", "SecurePass1", TestTenantId, CancellationToken.None);

        // Must succeed with 202-equivalent
        Assert.True(result.Succeeded);

        // Exactly one user created with correct role and email
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == "alice@example.com");
        Assert.NotNull(user);
        Assert.Equal(Role.Passenger, user!.Role);
        Assert.Equal(TestTenantId, user.TenantId);
        Assert.True(user.IsActive);
        Assert.False(string.IsNullOrWhiteSpace(user.PasswordHash));

        // Password must be stored hashed, not in plain text
        Assert.NotEqual("SecurePass1", user.PasswordHash);

        // OutboxMessage with correct type must exist
        var outbox = await db.OutboxMessages
            .SingleOrDefaultAsync(m => m.Type == "UserRegistered");
        Assert.NotNull(outbox);
        Assert.Contains(user.Id.ToString(), outbox!.Payload);
        Assert.Contains("alice@example.com", outbox.Payload);
        Assert.Contains("Passenger", outbox.Payload);
        Assert.Null(outbox.ProcessedAtUtc);
    }

    // Anti-enumeration — email existente retorna éxito sin crear duplicado ni outbox
    [Fact]
    public async Task Register_WhenEmailExists_ReturnsAcceptedWithoutDuplicate()
    {
        var dbName = $"register_dup_{Guid.NewGuid()}";
        await using var db = CreateInMemoryContext(dbName);
        await SeedTenantAsync(db);

        // Pre-seed existing user
        var existingUser = new User
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            Email = "bob@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OriginalPass1"),
            Role = Role.Passenger,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        };
        db.Users.Add(existingUser);
        await db.SaveChangesAsync();

        var outboxCountBefore = await db.OutboxMessages.CountAsync();

        var service = new AuthService(db, DefaultJwtSettings());
        var result = await service.RegisterAsync("bob@example.com", "AnotherPass2", TestTenantId, CancellationToken.None);

        // Must silently succeed (anti-enumeration)
        Assert.True(result.Succeeded);

        // No duplicate user created
        var userCount = await db.Users.CountAsync(u => u.Email == "bob@example.com");
        Assert.Equal(1, userCount);

        // No outbox message created for duplicate attempt
        var outboxCountAfter = await db.OutboxMessages.CountAsync();
        Assert.Equal(outboxCountBefore, outboxCountAfter);

        // Original password hash unchanged
        var unchanged = await db.Users.FindAsync(existingUser.Id);
        Assert.Equal(existingUser.PasswordHash, unchanged!.PasswordHash);
    }

    // TENANT_NOT_FOUND — registro rechazado si el tenant no existe
    [Fact]
    public async Task Register_WhenTenantNotFound_ReturnsFailure()
    {
        var dbName = $"register_no_tenant_{Guid.NewGuid()}";
        await using var db = CreateInMemoryContext(dbName);
        // Intentionally do NOT seed any tenant

        var service = new AuthService(db, DefaultJwtSettings());
        var result = await service.RegisterAsync("charlie@example.com", "Pass1234!", Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("TENANT_NOT_FOUND", result.ErrorCode);
    }

    // TENANT_INACTIVE — registro rechazado si el tenant está inactivo
    [Fact]
    public async Task Register_WhenTenantInactive_ReturnsFailure()
    {
        var dbName = $"register_inactive_tenant_{Guid.NewGuid()}";
        await using var db = CreateInMemoryContext(dbName);

        var inactiveTenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.Tenants.Add(new Tenant
        {
            Id = inactiveTenantId,
            Name = "Inactive Tenant",
            IsActive = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[] { 1 }
        });
        await db.SaveChangesAsync();

        var service = new AuthService(db, DefaultJwtSettings());
        var result = await service.RegisterAsync("dave@example.com", "Pass1234!", inactiveTenantId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("TENANT_INACTIVE", result.ErrorCode);
    }
}
