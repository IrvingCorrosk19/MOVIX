using MediatR;
using Movix.Application.Auth.Commands.Login;
using Movix.Application.Common.Models;

namespace Movix.Application.Auth.Commands.Refresh;

public record RefreshTokenCommand(string RefreshToken) : IRequest<Result<LoginResponse>>;
