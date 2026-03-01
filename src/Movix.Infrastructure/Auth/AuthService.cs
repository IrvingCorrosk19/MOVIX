using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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

    public async Task<Result<LoginResponse>> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive, cancellationToken);
        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return Result<LoginResponse>.Failure("Invalid credentials", "INVALID_CREDENTIALS");

        var (accessToken, expiresAt, expiresIn) = GenerateAccessToken(user);
        var (refreshTokenValue, _) = await CreateRefreshTokenAsync(user.Id, cancellationToken);

        return Result<LoginResponse>.Success(new LoginResponse(
            accessToken,
            refreshTokenValue,
            expiresAt,
            expiresIn));
    }

    public async Task<Result<LoginResponse>> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(refreshToken);
        var stored = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash && rt.ExpiresAtUtc > DateTime.UtcNow && rt.RevokedAtUtc == null, cancellationToken);

        if (stored == null)
            return Result<LoginResponse>.Failure("Invalid or expired refresh token", "REFRESH_TOKEN_INVALID");

        if (stored.ReplacedByTokenId != null)
            return Result<LoginResponse>.Failure("Refresh token reuse detected", "REFRESH_TOKEN_REUSE");

        stored.RevokedAtUtc = DateTime.UtcNow;
        stored.UpdatedAtUtc = DateTime.UtcNow;

        var (newRefreshTokenValue, newTokenId) = await CreateRefreshTokenAsync(stored.UserId, cancellationToken);
        stored.ReplacedByTokenId = newTokenId.ToString();

        var (accessToken, expiresAt, expiresIn) = GenerateAccessToken(stored.User);
        await _db.SaveChangesAsync(cancellationToken);

        return Result<LoginResponse>.Success(new LoginResponse(
            accessToken,
            newRefreshTokenValue,
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
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            _issuer,
            _audience,
            claims,
            expires: expiresAt,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt, expiresIn);
    }

    private async Task<(string Token, Guid EntityId)> CreateRefreshTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var hash = HashToken(token);
        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = hash,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _db.RefreshTokens.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return (token, entity.Id);
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
