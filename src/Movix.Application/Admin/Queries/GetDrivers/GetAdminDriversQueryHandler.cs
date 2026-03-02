using MediatR;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;
using Movix.Application.Drivers;

namespace Movix.Application.Admin.Queries.GetDrivers;

public class GetAdminDriversQueryHandler : IRequestHandler<GetAdminDriversQuery, Result<AdminDriversResponse>>
{
    private readonly IDriverRepository _driverRepository;
    private readonly ITenantContext _tenantContext;

    public GetAdminDriversQueryHandler(IDriverRepository driverRepository, ITenantContext tenantContext)
    {
        _driverRepository = driverRepository;
        _tenantContext = tenantContext;
    }

    public async Task<Result<AdminDriversResponse>> Handle(GetAdminDriversQuery request, CancellationToken cancellationToken)
    {
        // Fail-fast: Admin/Support must have a resolved tenant; SuperAdmin must set X-Tenant-Id.
        if (!_tenantContext.IsSuperAdmin && !_tenantContext.TenantId.HasValue)
            return Result<AdminDriversResponse>.Failure("Tenant context required", "TENANT_REQUIRED");

        var (drivers, total) = await _driverRepository.GetPagedAsync(request.Page, request.PageSize, cancellationToken);
        var dtos = drivers.Select(d => new AdminDriverDto(
            d.Id,
            d.UserId,
            d.Status.ToString(),
            d.IsVerified,
            d.CreatedAtUtc)).ToList();

        return Result<AdminDriversResponse>.Success(new AdminDriversResponse(dtos, total));
    }
}
