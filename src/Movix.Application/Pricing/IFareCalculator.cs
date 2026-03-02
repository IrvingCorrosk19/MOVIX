namespace Movix.Application.Pricing;

public interface IFareCalculator
{
    decimal Calculate(decimal baseFare, decimal pricePerKm, decimal pricePerMinute, decimal minimumFare, decimal distanceKm, decimal durationMinutes);
}
