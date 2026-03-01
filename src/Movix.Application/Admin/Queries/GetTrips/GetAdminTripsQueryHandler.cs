using MediatR;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;

namespace Movix.Application.Admin.Queries.GetTrips;

public class GetAdminTripsQueryHandler : IRequestHandler<GetAdminTripsQuery, Result<AdminTripsResponse>>
{
    private readonly IAdminTripRepository _adminTripRepository;

    public GetAdminTripsQueryHandler(IAdminTripRepository adminTripRepository)
    {
        _adminTripRepository = adminTripRepository;
    }

    public async Task<Result<AdminTripsResponse>> Handle(GetAdminTripsQuery request, CancellationToken cancellationToken)
    {
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
