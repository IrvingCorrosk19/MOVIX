using MediatR;
using Movix.Application.Common.Models;

namespace Movix.Application.Auth.Commands.Login;

public record LoginCommand(string Email, string Password) : IRequest<Result<LoginResponse>>;

public record LoginResponse(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAtUtc, int ExpiresInSeconds);
