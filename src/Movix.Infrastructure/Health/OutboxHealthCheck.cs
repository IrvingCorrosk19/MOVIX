using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Movix.Infrastructure.Persistence;

namespace Movix.Infrastructure.Health;

public sealed class OutboxHealthCheck : IHealthCheck
{
    private static readonly TimeSpan PendingStaleThreshold = TimeSpan.FromMinutes(5);
    private const int PendingDegradedThreshold = 100;
    private const int PendingUnhealthyThreshold = 1000;

    private readonly MovixDbContext _db;

    public OutboxHealthCheck(MovixDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var pending = await _db.OutboxMessages
            .CountAsync(x => x.ProcessedAtUtc == null && !x.IsDeadLetter, cancellationToken);
        var deadLetter = await _db.OutboxMessages
            .CountAsync(x => x.IsDeadLetter, cancellationToken);
        var stalePending = await _db.OutboxMessages
            .CountAsync(x => x.ProcessedAtUtc == null && !x.IsDeadLetter && x.CreatedAtUtc < DateTime.UtcNow - PendingStaleThreshold, cancellationToken);

        var data = new Dictionary<string, object>
        {
            ["pending"] = pending,
            ["deadLetter"] = deadLetter,
            ["stalePending"] = stalePending
        };

        if (pending > PendingUnhealthyThreshold)
            return HealthCheckResult.Unhealthy("Outbox pending count exceeds 1000.", data: data);
        if (deadLetter > 0 || pending > PendingDegradedThreshold || stalePending > 0)
            return HealthCheckResult.Degraded("Outbox has dead letters, high pending, or stale messages.", data: data);

        return HealthCheckResult.Healthy("Outbox is healthy.", data: data);
    }
}
