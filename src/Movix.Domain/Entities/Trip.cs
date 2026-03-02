using Movix.Domain.Common;
using Movix.Domain.Enums;
using NetTopologySuite.Geometries;

namespace Movix.Domain.Entities;

public class Trip : IAuditableEntity, IConcurrencyEntity
{
    public Guid Id { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid PassengerId { get; set; }
    public Guid? DriverId { get; set; }
    public Guid? VehicleId { get; set; }
    public TripStatus Status { get; set; }
    public Point? PickupLocation { get; set; }
    public Point? DropoffLocation { get; set; }
    public string? PickupAddress { get; set; }
    public string? DropoffAddress { get; set; }
    public Guid? TenantId { get; set; }
    public decimal? EstimatedAmount { get; set; }
    public decimal? FinalAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal? DistanceKm { get; set; }
    public decimal? DurationMinutes { get; set; }
    public Guid? TariffPlanIdUsed { get; set; }
    public decimal? BaseFareUsed { get; set; }
    public decimal? PricePerKmUsed { get; set; }
    public decimal? PricePerMinuteUsed { get; set; }
    public decimal? MinimumFareUsed { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = null!;

    public User Passenger { get; set; } = null!;
    public Driver? Driver { get; set; }
    public Vehicle? Vehicle { get; set; }
    public ICollection<TripStatusHistory> StatusHistory { get; set; } = new List<TripStatusHistory>();
    public Payment? Payment { get; set; }
    public ICollection<Rating> Ratings { get; set; } = new List<Rating>();
}
