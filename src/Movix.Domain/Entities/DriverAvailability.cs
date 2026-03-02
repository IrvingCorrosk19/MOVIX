using Movix.Domain.Common;

namespace Movix.Domain.Entities;

public class DriverAvailability : IConcurrencyEntity
{
    public Guid DriverId { get; set; }
    public bool IsOnline { get; set; }
    public Guid? CurrentTripId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = null!;

    public Driver Driver { get; set; } = null!;
}
