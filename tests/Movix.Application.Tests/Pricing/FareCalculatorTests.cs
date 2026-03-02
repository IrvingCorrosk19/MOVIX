using Movix.Application.Pricing;
using Xunit;

namespace Movix.Application.Tests.Pricing;

public class FareCalculatorTests
{
    private readonly IFareCalculator _calculator = new FareCalculator();

    [Fact]
    public void Calculate_BaseCase_ReturnsSubtotal()
    {
        var result = _calculator.Calculate(10m, 2m, 0.5m, 5m, 5m, 10m);
        Assert.Equal(25.00m, result); // 10 + 2*5 + 0.5*10 = 10+10+5 = 25
    }

    [Fact]
    public void Calculate_SubtotalBelowMinimum_ReturnsMinimumFare()
    {
        var result = _calculator.Calculate(1m, 0m, 0m, 15m, 0m, 0m);
        Assert.Equal(15.00m, result);
    }

    [Fact]
    public void Calculate_ZeroDistanceAndDuration_ReturnsMaxOfBaseFareAndMinimum()
    {
        var result = _calculator.Calculate(20m, 1m, 1m, 10m, 0m, 0m);
        Assert.Equal(20.00m, result);
    }

    [Fact]
    public void Calculate_Rounding_MidpointAwayFromZero()
    {
        var result = _calculator.Calculate(10m, 1.115m, 0m, 0m, 1m, 0m);
        Assert.Equal(11.12m, result);
    }

    [Fact]
    public void Calculate_LargeValues_ComputesCorrectly()
    {
        var result = _calculator.Calculate(100m, 5m, 2m, 50m, 100m, 60m);
        Assert.Equal(720.00m, result); // 100 + 500 + 120 = 720
    }

    [Fact]
    public void Calculate_AllZeros_ReturnsZero()
    {
        var result = _calculator.Calculate(0m, 0m, 0m, 0m, 0m, 0m);
        Assert.Equal(0.00m, result);
    }
}
