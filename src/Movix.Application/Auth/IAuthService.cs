using Movix.Application.Auth.Commands.Login;
using Movix.Application.Common.Models;

namespace Movix.Application.Auth;

public interface IAuthService
{
    Task<Result<LoginResponse>> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<Result<LoginResponse>> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task<Result> LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default);
}
