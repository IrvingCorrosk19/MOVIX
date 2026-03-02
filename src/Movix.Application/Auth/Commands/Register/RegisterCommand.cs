using MediatR;
using Movix.Application.Common.Models;

namespace Movix.Application.Auth.Commands.Register;

public record RegisterCommand(string Email, string Password, Guid TenantId) : IRequest<Result>;
