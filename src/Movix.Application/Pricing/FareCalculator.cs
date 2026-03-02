using System.Globalization;

namespace Movix.Application.Pricing;

public sealed class FareCalculator : IFareCalculator
{
    private static readonly MidpointRounding Rounding = MidpointRounding.AwayFromZero;
    private const int DecimalPlaces = 2;

    public decimal Calculate(decimal baseFare, decimal pricePerKm, decimal pricePerMinute, decimal minimumFare, decimal distanceKm, decimal durationMinutes)
    {
        var subtotal = baseFare + (pricePerKm * distanceKm) + (pricePerMinute * durationMinutes);
        var total = Math.Max(subtotal, minimumFare);
        return Math.Round(total, DecimalPlaces, Rounding);
    }
}
