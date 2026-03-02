using FluentValidation;

namespace Movix.Application.Pricing.Queries.QuoteFare;

public class QuoteFareQueryValidator : AbstractValidator<QuoteFareQuery>
{
    public QuoteFareQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.DistanceKm).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DurationMinutes).GreaterThanOrEqualTo(0);
    }
}
