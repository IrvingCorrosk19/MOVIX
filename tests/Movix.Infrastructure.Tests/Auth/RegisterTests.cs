using Microsoft.EntityFrameworkCore;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Movix.Infrastructure.Auth;
using Movix.Infrastructure.Persistence;
using Xunit;

namespace Movix.Infrastructure.Tests.Auth;

public class RegisterTests
{
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

    // F-1 — Registro exitoso: crea usuario Passenger + evento en outbox
    [Fact]
    public async Task Register_NewUser_CreatesUserAndOutbox()
    {
        var dbName = $"register_new_{Guid.NewGuid()}";
        await using var db = CreateInMemoryContext(dbName);

        var service = new AuthService(db, DefaultJwtSettings());
        var result = await service.RegisterAsync("alice@example.com", "SecurePass1", CancellationToken.None);

        // Must succeed with 202-equivalent
        Assert.True(result.Succeeded);

        // Exactly one user created with correct role and email
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == "alice@example.com");
        Assert.NotNull(user);
        Assert.Equal(Role.Passenger, user!.Role);
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

        // Pre-seed existing user
        var existingUser = new User
        {
            Id = Guid.NewGuid(),
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
        var result = await service.RegisterAsync("bob@example.com", "AnotherPass2", CancellationToken.None);

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
}
