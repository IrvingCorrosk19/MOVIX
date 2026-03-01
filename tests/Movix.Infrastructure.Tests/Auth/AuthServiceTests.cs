using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Movix.Infrastructure.Auth;
using Movix.Infrastructure.Persistence;
using Xunit;

namespace Movix.Infrastructure.Tests.Auth;

public class AuthServiceTests
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

    private static string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }

    private static User MakeUser(Guid id) => new()
    {
        Id = id,
        Email = $"{id}@test.com",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("pass"),
        Role = Role.Passenger,
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        RowVersion = new byte[] { 1 }
    };

    // R-4 — Reuse detectado invalida familia completa
    [Fact]
    public async Task Refresh_WhenReuseDetected_RevokesEntireFamily()
    {
        var dbName = $"auth_reuse_{Guid.NewGuid()}";
        await using var db = CreateInMemoryContext(dbName);

        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.Users.Add(MakeUser(userId));

        // Victim token — already consumed (revoked), attacker replays it
        var rawVictimToken = "victim-raw-token-value";
        var victimHash = HashToken(rawVictimToken);
        var victimToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = victimHash,
            ExpiresAtUtc = now.AddDays(7),
            RevokedAtUtc = now.AddMinutes(-1),   // already revoked = reuse scenario
            RevocationReason = "Rotated",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        // Successor token still active — must be revoked when reuse detected
        var successorToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = HashToken("successor-raw"),
            ExpiresAtUtc = now.AddDays(7),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.RefreshTokens.AddRange(victimToken, successorToken);
        await db.SaveChangesAsync();

        var service = new AuthService(db, DefaultJwtSettings());
        var result = await service.RefreshTokenAsync(rawVictimToken);

        // Must fail with REUSE code
        Assert.False(result.Succeeded);
        Assert.Equal("REFRESH_TOKEN_REUSE", result.ErrorCode);

        // Entire family must be revoked (successor is now revoked too)
        var familyTokens = await db.RefreshTokens
            .Where(rt => rt.FamilyId == familyId)
            .ToListAsync();

        Assert.All(familyTokens, t => Assert.NotNull(t.RevokedAtUtc));
        Assert.All(familyTokens.Where(t => t.Id == successorToken.Id),
            t => Assert.Equal("Reuse detected", t.RevocationReason));
    }

    // B-1 — Flujo normal de rotación: atómico, un solo SaveChanges, nuevo token con mismo FamilyId
    [Fact]
    public async Task Refresh_NormalRotation_Succeeds_WithSameFamilyId()
    {
        var dbName = $"auth_rotation_{Guid.NewGuid()}";
        await using var db = CreateInMemoryContext(dbName);

        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.Users.Add(MakeUser(userId));

        var rawToken = "active-raw-token-value";
        var activeToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = HashToken(rawToken),
            ExpiresAtUtc = now.AddDays(7),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.RefreshTokens.Add(activeToken);
        await db.SaveChangesAsync();

        var service = new AuthService(db, DefaultJwtSettings());
        var result = await service.RefreshTokenAsync(rawToken);

        // Must succeed
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.False(string.IsNullOrWhiteSpace(result.Data!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.Data.RefreshToken));
        Assert.NotEqual(rawToken, result.Data.RefreshToken);

        // Old token must be revoked with reason "Rotated"
        var oldToken = await db.RefreshTokens.FindAsync(activeToken.Id);
        Assert.NotNull(oldToken!.RevokedAtUtc);
        Assert.Equal("Rotated", oldToken.RevocationReason);
        Assert.NotNull(oldToken.ReplacedByTokenId);

        // New token must exist and share the same FamilyId
        var newTokenHash = HashToken(result.Data.RefreshToken);
        var newToken = await db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == newTokenHash);
        Assert.NotNull(newToken);
        Assert.Equal(familyId, newToken!.FamilyId);
        Assert.Null(newToken.RevokedAtUtc);
    }
}
