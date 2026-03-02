using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Movix.Application.Auth;
using Movix.Application.Auth.Commands.Login;
using Movix.Application.Common.Models;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Movix.Infrastructure.Persistence;

namespace Movix.Infrastructure.Auth;

public class AuthService : IAuthService
{
    private readonly MovixDbContext _db;
    private readonly JwtSettings _jwtSettings;
    private readonly string _issuer;
    private readonly string _audience;

    public AuthService(MovixDbContext db, JwtSettings jwtSettings)
    {
        _db = db;
        _jwtSettings = jwtSettings;
        _issuer = _jwtSettings.Issuer ?? "movix";
        _audience = _jwtSettings.Audience ?? "movix";
    }

    public async Task<Result> RegisterAsync(string email, string password, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Verify tenant exists and is active
        var tenant = await _db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant == null)
            return Result.Failure("Tenant not found", "TENANT_NOT_FOUND");
        if (!tenant.IsActive)
            return Result.Failure("Tenant is inactive", "TENANT_INACTIVE");

        // Anti-enumeration: never reveal whether the email already exists
        var exists = await _db.Users.AnyAsync(u => u.Email == email, cancellationToken);
        if (exists)
            return Result.Success();

        var userId = Guid.NewGuid();

        var user = new User
        {
            Id = userId,
            TenantId = tenantId,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = Role.Passenger,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[] { 1 }
        };

        var payload = JsonSerializer.Serialize(new
        {
            userId,
            email,
            role = Role.Passenger.ToString(),
            occurredAtUtc = now
        });

        var outbox = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "UserRegistered",
            Payload = payload,
            CreatedAtUtc = now
        };

        _db.Users.Add(user);
        _db.OutboxMessages.Add(outbox);
        await _db.SaveChangesAsync(cancellationToken); // Single SaveChanges

        return Result.Success();
    }

    public async Task<Result<LoginResponse>> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive, cancellationToken);
        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return Result<LoginResponse>.Failure("Invalid credentials", "INVALID_CREDENTIALS");

        var familyId = Guid.NewGuid();
        var (rawToken, tokenEntity) = BuildRefreshToken(user.Id, familyId);
        _db.RefreshTokens.Add(tokenEntity);

        var (accessToken, expiresAt, expiresIn) = GenerateAccessToken(user);
        await _db.SaveChangesAsync(cancellationToken);

        return Result<LoginResponse>.Success(new LoginResponse(
            accessToken,
            rawToken,
            expiresAt,
            expiresIn));
    }

    public async Task<Result<LoginResponse>> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(refreshToken);

        // Query without RevokedAtUtc filter to detect reuse of already-consumed tokens
        var stored = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);

        if (stored == null)
            return Result<LoginResponse>.Failure("Invalid or expired refresh token", "REFRESH_TOKEN_INVALID");

        // Reuse detected: token was already consumed — invalidate entire family in one SaveChanges
        if (stored.RevokedAtUtc != null)
        {
            await RevokeTokenFamilyAsync(stored.FamilyId, "Reuse detected", cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return Result<LoginResponse>.Failure("Refresh token reuse detected", "REFRESH_TOKEN_REUSE");
        }

        if (stored.ExpiresAtUtc <= DateTime.UtcNow)
            return Result<LoginResponse>.Failure("Invalid or expired refresh token", "REFRESH_TOKEN_INVALID");

        // Normal rotation: build successor token (same family), mark predecessor revoked — one SaveChanges
        var now = DateTime.UtcNow;
        var (newRawToken, newTokenEntity) = BuildRefreshToken(stored.UserId, stored.FamilyId);
        _db.RefreshTokens.Add(newTokenEntity);

        stored.RevokedAtUtc = now;
        stored.UpdatedAtUtc = now;
        stored.ReplacedByTokenId = newTokenEntity.Id.ToString();
        stored.RevocationReason = "Rotated";

        var (accessToken, expiresAt, expiresIn) = GenerateAccessToken(stored.User);
        await _db.SaveChangesAsync(cancellationToken);

        return Result<LoginResponse>.Success(new LoginResponse(
            accessToken,
            newRawToken,
            expiresAt,
            expiresIn));
    }

    public async Task<Result> LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return Result.Success();

        var tokenHash = HashToken(refreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);
        if (stored != null)
        {
            stored.RevokedAtUtc = DateTime.UtcNow;
            stored.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    private (string Token, DateTime ExpiresAt, int ExpiresIn) GenerateAccessToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey ?? throw new InvalidOperationException("JWT SecretKey not set")));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);
        var expiresIn = (int)(expiresAt - DateTime.UtcNow).TotalSeconds;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("tenant_id", user.TenantId.ToString())
        };

        var token = new JwtSecurityToken(
            _issuer,
            _audience,
            claims,
            expires: expiresAt,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt, expiresIn);
    }

    private async Task RevokeTokenFamilyAsync(Guid familyId, string reason, CancellationToken cancellationToken)
    {
        var activeTokens = await _db.RefreshTokens
            .Where(rt => rt.FamilyId == familyId && rt.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var token in activeTokens)
        {
            token.RevokedAtUtc = now;
            token.UpdatedAtUtc = now;
            token.RevocationReason = reason;
        }
        // Caller is responsible for the single SaveChanges
    }

    private (string RawToken, RefreshToken Entity) BuildRefreshToken(Guid userId, Guid familyId)
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = HashToken(rawToken),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        return (rawToken, entity);
    }

    private static string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}

public class JwtSettings
{
    public const string SectionName = "Jwt";
    public string? SecretKey { get; set; }
    public string? Issuer { get; set; }
    public string? Audience { get; set; }
    public int AccessTokenExpirationMinutes { get; set; } = 15;
    public int RefreshTokenExpirationDays { get; set; } = 7;
}
