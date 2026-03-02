using MediatR;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;
using Movix.Application.Pricing.Commands.CreateTariffPlan;

namespace Movix.Application.Pricing.Queries.GetActiveTariffPlan;

public class GetActiveTariffPlanQueryHandler : IRequestHandler<GetActiveTariffPlanQuery, Result<TariffPlanDto>>
{
    private readonly ITariffPlanRepository _repository;
    private readonly IDateTimeService _dateTime;

    public GetActiveTariffPlanQueryHandler(ITariffPlanRepository repository, IDateTimeService dateTime)
    {
        _repository = repository;
        _dateTime = dateTime;
    }

    public async Task<Result<TariffPlanDto>> Handle(GetActiveTariffPlanQuery request, CancellationToken cancellationToken)
    {
        var atUtc = _dateTime.UtcNow;
        var plan = await _repository.GetApplicableTariffAsync(request.TenantId, atUtc, cancellationToken);
        if (plan == null)
            return Result<TariffPlanDto>.Failure("No applicable tariff plan for tenant at this time", "NO_APPLICABLE_TARIFF");
        return Result<TariffPlanDto>.Success(Map(plan));
    }

    private static TariffPlanDto Map(Domain.Entities.TariffPlan p) => new(
        p.Id, p.TenantId, p.Name, p.Currency, p.BaseFare, p.PricePerKm, p.PricePerMinute, p.MinimumFare,
        p.IsActive, p.Priority, p.EffectiveFromUtc, p.EffectiveUntilUtc, p.CreatedAtUtc);
}
