namespace Movix.Domain.Entities;

public class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public string? Error { get; set; }
    public int AttemptCount { get; private set; }
    public DateTime? LastAttemptUtc { get; private set; }

    public void RecordFailedAttempt()
    {
        AttemptCount++;
        LastAttemptUtc = DateTime.UtcNow;
    }
}
