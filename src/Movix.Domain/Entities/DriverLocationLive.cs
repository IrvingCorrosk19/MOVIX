using Movix.Domain.Common;
using NetTopologySuite.Geometries;

namespace Movix.Domain.Entities;

public class DriverLocationLive : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid DriverId { get; set; }
    public Point Location { get; set; } = null!;
    public double? Heading { get; set; }
    public double? Speed { get; set; }
    public DateTime RecordedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    public Driver Driver { get; set; } = null!;
}
