using Movix.Domain.Common;

namespace Movix.Domain.Entities;

public class TariffPlan : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public decimal BaseFare { get; set; }
    public decimal PricePerKm { get; set; }
    public decimal PricePerMinute { get; set; }
    public decimal MinimumFare { get; set; }
    public bool IsActive { get; set; }
    public int Priority { get; set; } = 100;
    public DateTime? EffectiveFromUtc { get; set; }
    public DateTime? EffectiveUntilUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
