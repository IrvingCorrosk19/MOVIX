using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Movix.Infrastructure.Persistence;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Movix.Infrastructure.Messaging;

public sealed class OutboxProcessor
{
    private static readonly ActivitySource ActivitySource = new("Movix.Outbox");
    private static readonly Meter Meter = new("Movix.Outbox");
    private static readonly Counter<long> ProcessedCounter = Meter.CreateCounter<long>("outbox_processed_total");
    private static readonly Counter<long> FailedCounter = Meter.CreateCounter<long>("outbox_failed_total");
    private static readonly Counter<long> AttemptCounter = Meter.CreateCounter<long>("outbox_attempt_total");
    private static readonly Counter<long> DeadLetterCounter = Meter.CreateCounter<long>("outbox_deadletter_total");

    private const int MaxAttempts = 5;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// SELECT pending rows and lock them so no other worker can take the same rows (multi-pod safe).
    /// Uses PostgreSQL FOR UPDATE SKIP LOCKED. Must be called within an open transaction.
    /// </summary>
    private const string SelectPendingForUpdateSql = """
        SELECT "Id", "EventId", "CorrelationId", "Type", "Payload", "CreatedAtUtc", "ProcessedAtUtc", "Error", "AttemptCount", "LastAttemptUtc", "IsDeadLetter", "DeadLetteredAtUtc"
        FROM outbox_messages
        WHERE "ProcessedAtUtc" IS NULL AND "IsDeadLetter" = false AND "AttemptCount" < 5
        ORDER BY "CreatedAtUtc"
        LIMIT {0}
        FOR UPDATE SKIP LOCKED
        """;

    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(IOptions<OutboxOptions> options, ILogger<OutboxProcessor> logger)
    {
        _options = options?.Value ?? new OutboxOptions { MaxBatchSize = 50 };
        _logger = logger;
    }

    public async Task ProcessOnceAsync(MovixDbContext db, IEventPublisher publisher, CancellationToken cancellationToken = default)
    {
        var batchSize = _options.MaxBatchSize > 0 ? _options.MaxBatchSize : 50;
        List<Movix.Domain.Entities.OutboxMessage> messages;

        if (db.Database.IsNpgsql())
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            messages = await db.OutboxMessages
                .FromSqlRaw(SelectPendingForUpdateSql, batchSize)
                .ToListAsync(cancellationToken);
            if (messages.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            await ProcessBatchAsync(messages, publisher, DateTime.UtcNow, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        // Fallback for InMemory / other providers (e.g. unit tests)
        messages = await db.OutboxMessages
            .Where(x => x.ProcessedAtUtc == null && !x.IsDeadLetter && x.AttemptCount < MaxAttempts)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
            return;

        await ProcessBatchAsync(messages, publisher, DateTime.UtcNow, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessBatchAsync(
        List<Movix.Domain.Entities.OutboxMessage> messages,
        IEventPublisher publisher,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            var delay = TimeSpan.FromSeconds(BaseDelay.TotalSeconds * Math.Pow(2, message.AttemptCount));
            if (message.LastAttemptUtc != null && nowUtc < message.LastAttemptUtc.Value + delay)
                continue;

            try
            {
                using var activity = ActivitySource.StartActivity("Outbox.Publish", ActivityKind.Producer);
                activity?.SetTag("event.id", message.EventId);
                activity?.SetTag("event.type", message.Type);
                activity?.SetTag("outbox.attempt", message.AttemptCount);
                activity?.SetTag("correlation.id", message.CorrelationId);

                await publisher.PublishAsync(message.EventId, message.Type, message.Payload, message.CorrelationId, cancellationToken);

                message.ProcessedAtUtc = DateTime.UtcNow;
                ProcessedCounter.Add(1);
                _logger.LogDebug(
                    "Outbox processed: EventId={EventId}, OutboxMessageId={OutboxMessageId}, Type={Type}, AttemptCount={AttemptCount}, IsDeadLetter={IsDeadLetter}",
                    message.EventId, message.Id, message.Type, message.AttemptCount, message.IsDeadLetter);
            }
            catch (Exception ex)
            {
                AttemptCounter.Add(1);
                FailedCounter.Add(1);
                _logger.LogError(ex,
                    "Outbox failed: EventId={EventId}, OutboxMessageId={OutboxMessageId}, Type={Type}, AttemptCount={AttemptCount}, IsDeadLetter={IsDeadLetter}",
                    message.EventId, message.Id, message.Type, message.AttemptCount, message.IsDeadLetter);

                message.RecordFailedAttempt();
                if (message.AttemptCount >= MaxAttempts)
                {
                    message.MarkAsDeadLetter();
                    DeadLetterCounter.Add(1);
                }
            }
        }
    }
}
