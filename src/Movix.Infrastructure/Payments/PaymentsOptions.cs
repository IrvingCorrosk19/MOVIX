namespace Movix.Infrastructure.Payments;

public class PaymentsOptions
{
    public const string SectionName = "Payments";
    public string Mode { get; set; } = "Stripe";
    public SimulationOptions Simulation { get; set; } = new();
}

public class SimulationOptions
{
    public string DefaultResult { get; set; } = "success";
}
