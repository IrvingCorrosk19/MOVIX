using MediatR;
using Movix.Application.Common.Models;

namespace Movix.Application.Auth.Commands.Logout;

public record LogoutCommand(string? RefreshToken) : IRequest<Result>;
