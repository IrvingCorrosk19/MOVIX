using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Movix.Application.Auth.Commands.Login;
using Movix.Application.Auth.Commands.Logout;
using Movix.Application.Auth.Commands.Refresh;
using Movix.Application.Auth.Commands.Register;

namespace Movix.Api.Controllers.V1;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Register a new passenger account for a given tenant.
    /// Returns 202 Accepted on success (email enumeration prevention).
    /// Returns 400 if tenant is invalid or inactive.
    /// </summary>
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new RegisterCommand(request.Email, request.Password, request.TenantId), ct);
        if (!result.Succeeded)
            return BadRequest(new { error = result.Error, code = result.ErrorCode });
        return Accepted();
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new LoginCommand(request.Email, request.Password), ct);
        if (!result.Succeeded)
            return Unauthorized(new { error = result.Error, code = result.ErrorCode });
        return Ok(result.Data);
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new RefreshTokenCommand(request.RefreshToken), ct);
        if (!result.Succeeded)
            return Unauthorized(new { error = result.Error, code = result.ErrorCode });
        return Ok(result.Data);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? request, CancellationToken ct)
    {
        await _mediator.Send(new LogoutCommand(request?.RefreshToken), ct);
        return Ok();
    }
}

public record RegisterRequest(string Email, string Password, Guid TenantId);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record LogoutRequest(string? RefreshToken);
