using MediatR;
using Movix.Application.Auth.Commands.Login;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;

namespace Movix.Application.Auth.Commands.Refresh;

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, Result<LoginResponse>>
{
    private readonly IAuthService _authService;

    public RefreshTokenCommandHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<Result<LoginResponse>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        return await _authService.RefreshTokenAsync(request.RefreshToken, cancellationToken);
    }
}
