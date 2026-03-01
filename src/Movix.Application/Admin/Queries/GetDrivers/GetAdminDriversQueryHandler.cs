using MediatR;
using Movix.Application.Common.Models;
using Movix.Application.Drivers;

namespace Movix.Application.Admin.Queries.GetDrivers;

public class GetAdminDriversQueryHandler : IRequestHandler<GetAdminDriversQuery, Result<AdminDriversResponse>>
{
    private readonly IDriverRepository _driverRepository;

    public GetAdminDriversQueryHandler(IDriverRepository driverRepository)
    {
        _driverRepository = driverRepository;
    }

    public async Task<Result<AdminDriversResponse>> Handle(GetAdminDriversQuery request, CancellationToken cancellationToken)
    {
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
