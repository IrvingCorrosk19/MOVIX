namespace Movix.Infrastructure.Messaging;

public class OutboxOptions
{
    public const string SectionName = "Outbox";

    public bool Enabled { get; set; }
    public int PollIntervalMs { get; set; } = 500;
    public int MaxBatchSize { get; set; } = 50;
    public int ErrorBackoffMs { get; set; } = 2000;
    public int JitterMaxMs { get; set; } = 500;
}
