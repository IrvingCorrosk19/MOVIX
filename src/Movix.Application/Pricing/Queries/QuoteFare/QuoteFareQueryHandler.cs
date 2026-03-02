using MediatR;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;
using Movix.Application.Pricing;

namespace Movix.Application.Pricing.Queries.QuoteFare;

public class QuoteFareQueryHandler : IRequestHandler<QuoteFareQuery, Result<QuoteFareDto>>
{
    private readonly ITariffPlanRepository _tariffRepository;
    private readonly IFareCalculator _calculator;
    private readonly IDateTimeService _dateTime;

    public QuoteFareQueryHandler(ITariffPlanRepository tariffRepository, IFareCalculator calculator, IDateTimeService dateTime)
    {
        _tariffRepository = tariffRepository;
        _calculator = calculator;
        _dateTime = dateTime;
    }

    public async Task<Result<QuoteFareDto>> Handle(QuoteFareQuery request, CancellationToken cancellationToken)
    {
        var atUtc = _dateTime.UtcNow;
        var plan = await _tariffRepository.GetApplicableTariffAsync(request.TenantId, atUtc, cancellationToken);
        if (plan == null)
            return Result<QuoteFareDto>.Failure("No applicable tariff plan for tenant at this time", "NO_APPLICABLE_TARIFF");

        var fare = _calculator.Calculate(
            plan.BaseFare, plan.PricePerKm, plan.PricePerMinute, plan.MinimumFare,
            request.DistanceKm, request.DurationMinutes);
        return Result<QuoteFareDto>.Success(new QuoteFareDto(fare, plan.Currency, plan.Id));
    }
}
