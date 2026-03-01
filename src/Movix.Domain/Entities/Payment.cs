using Movix.Domain.Common;
using Movix.Domain.Enums;

namespace Movix.Domain.Entities;

public class Payment : IAuditableEntity, IConcurrencyEntity
{
    public Guid Id { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid TripId { get; set; }
    public Guid PayerId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public PaymentStatus Status { get; set; }
    public string? ExternalPaymentId { get; set; }
    public string? FailureReason { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = null!;

    public Trip Trip { get; set; } = null!;
}
