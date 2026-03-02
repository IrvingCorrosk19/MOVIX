using MediatR;
using Movix.Application.Common.Models;

namespace Movix.Application.Tenants.Commands.CreateTenant;

public record CreateTenantCommand(string Name) : IRequest<Result<TenantDto>>;

public record TenantDto(Guid Id, string Name, bool IsActive, DateTime CreatedAtUtc);
