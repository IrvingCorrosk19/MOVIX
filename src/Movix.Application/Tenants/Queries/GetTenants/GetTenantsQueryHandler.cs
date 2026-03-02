using MediatR;
using Movix.Application.Common.Models;
using Movix.Application.Tenants;

namespace Movix.Application.Tenants.Queries.GetTenants;

public class GetTenantsQueryHandler : IRequestHandler<GetTenantsQuery, Result<GetTenantsResponse>>
{
    private readonly ITenantRepository _tenantRepository;

    public GetTenantsQueryHandler(ITenantRepository tenantRepository)
    {
        _tenantRepository = tenantRepository;
    }

    public async Task<Result<GetTenantsResponse>> Handle(GetTenantsQuery request, CancellationToken cancellationToken)
    {
        var tenants = await _tenantRepository.GetAllAsync(cancellationToken);
        var items = tenants.Select(t => new TenantListItemDto(t.Id, t.Name, t.IsActive)).ToList();
        return Result<GetTenantsResponse>.Success(new GetTenantsResponse(items));
    }
}
