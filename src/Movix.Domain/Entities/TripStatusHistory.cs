using Movix.Domain.Common;
using Movix.Domain.Enums;

namespace Movix.Domain.Entities;

public class TripStatusHistory : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public TripStatus FromStatus { get; set; }
    public TripStatus ToStatus { get; set; }
    public string? Reason { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    public Trip Trip { get; set; } = null!;
}
