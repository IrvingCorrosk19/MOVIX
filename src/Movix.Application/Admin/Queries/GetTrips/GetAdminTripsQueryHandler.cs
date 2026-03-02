using MediatR;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;

namespace Movix.Application.Admin.Queries.GetTrips;

public class GetAdminTripsQueryHandler : IRequestHandler<GetAdminTripsQuery, Result<AdminTripsResponse>>
{
    private readonly IAdminTripRepository _adminTripRepository;
    private readonly ITenantContext _tenantContext;

    public GetAdminTripsQueryHandler(IAdminTripRepository adminTripRepository, ITenantContext tenantContext)
    {
        _adminTripRepository = adminTripRepository;
        _tenantContext = tenantContext;
    }

    public async Task<Result<AdminTripsResponse>> Handle(GetAdminTripsQuery request, CancellationToken cancellationToken)
    {
        // Fail-fast: Admin/Support must have a resolved tenant; SuperAdmin must set X-Tenant-Id.
        if (!_tenantContext.IsSuperAdmin && !_tenantContext.TenantId.HasValue)
            return Result<AdminTripsResponse>.Failure("Tenant context required", "TENANT_REQUIRED");

        var (trips, total) = await _adminTripRepository.GetPagedAsync(request.Page, request.PageSize, cancellationToken);
        var dtos = trips.Select(t => new AdminTripDto(
            t.Id,
            t.Status.ToString(),
            t.PassengerId,
            t.DriverId,
            t.PickupAddress,
            t.DropoffAddress,
            t.FinalAmount,
            t.CreatedAtUtc)).ToList();

        return Result<AdminTripsResponse>.Success(new AdminTripsResponse(dtos, total));
    }
}
