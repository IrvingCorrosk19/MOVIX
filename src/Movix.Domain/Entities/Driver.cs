using Movix.Domain.Common;
using Movix.Domain.Enums;

namespace Movix.Domain.Entities;

public class Driver : IAuditableEntity, IConcurrencyEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DriverStatus Status { get; set; }
    public bool IsVerified { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = null!;

    public User User { get; set; } = null!;
    public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
    public ICollection<DriverLocationLive> LocationHistory { get; set; } = new List<DriverLocationLive>();
}
