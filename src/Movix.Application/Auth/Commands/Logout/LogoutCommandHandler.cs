using MediatR;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;

namespace Movix.Application.Auth.Commands.Logout;

public class LogoutCommandHandler : IRequestHandler<LogoutCommand, Result>
{
    private readonly IAuthService _authService;

    public LogoutCommandHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        return await _authService.LogoutAsync(request.RefreshToken, cancellationToken);
    }
}
