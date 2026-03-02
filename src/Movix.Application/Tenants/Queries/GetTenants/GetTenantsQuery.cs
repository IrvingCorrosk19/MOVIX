using MediatR;
using Movix.Application.Common.Models;

namespace Movix.Application.Tenants.Queries.GetTenants;

public record GetTenantsQuery : IRequest<Result<GetTenantsResponse>>;

public record GetTenantsResponse(IReadOnlyList<TenantListItemDto> Tenants);

public record TenantListItemDto(Guid Id, string Name, bool IsActive);
