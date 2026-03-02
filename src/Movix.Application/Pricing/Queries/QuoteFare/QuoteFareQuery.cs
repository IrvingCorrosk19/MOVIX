using MediatR;
using Movix.Application.Common.Models;

namespace Movix.Application.Pricing.Queries.QuoteFare;

public record QuoteFareQuery(Guid TenantId, decimal DistanceKm, decimal DurationMinutes) : IRequest<Result<QuoteFareDto>>;

public record QuoteFareDto(decimal FareAmount, string Currency, Guid TariffPlanId);
