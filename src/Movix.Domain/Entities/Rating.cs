using Movix.Domain.Common;

namespace Movix.Domain.Entities;

public class Rating : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public Guid RaterId { get; set; }
    public Guid RatedId { get; set; }
    public int Score { get; set; }
    public string? Comment { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    public Trip Trip { get; set; } = null!;
}
