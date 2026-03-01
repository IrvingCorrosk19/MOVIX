using MediatR;
using Movix.Application.Common.Models;

namespace Movix.Application.Admin.Queries.GetDrivers;

public record GetAdminDriversQuery(int Page = 1, int PageSize = 20) : IRequest<Result<AdminDriversResponse>>;

public record AdminDriversResponse(IReadOnlyList<AdminDriverDto> Drivers, int TotalCount);

public record AdminDriverDto(Guid Id, Guid UserId, string Status, bool IsVerified, DateTime CreatedAtUtc);
