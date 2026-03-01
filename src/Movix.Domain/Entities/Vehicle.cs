using Movix.Domain.Common;

namespace Movix.Domain.Entities;

public class Vehicle : IAuditableEntity, IConcurrencyEntity
{
    public Guid Id { get; set; }
    public Guid DriverId { get; set; }
    public string Plate { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = null!;

    public Driver Driver { get; set; } = null!;
}
