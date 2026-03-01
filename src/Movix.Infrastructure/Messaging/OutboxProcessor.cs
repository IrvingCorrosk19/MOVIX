using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Movix.Infrastructure.Persistence;

namespace Movix.Infrastructure.Messaging;

public sealed class OutboxProcessor : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("Movix.Outbox");
    private static readonly Meter Meter = new("Movix.Outbox");
    private static readonly Counter<long> ProcessedCounter = Meter.CreateCounter<long>("outbox_processed_total");
    private static readonly Counter<long> FailedCounter = Meter.CreateCounter<long>("outbox_failed_total");
    private static readonly Counter<long> AttemptCounter = Meter.CreateCounter<long>("outbox_attempt_total");
    private static readonly Counter<long> DeadLetterCounter = Meter.CreateCounter<long>("outbox_deadletter_total");

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 50;
    private const int MaxAttempts = 5;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<MovixDbContext>();
                var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
                await ProcessOnceAsync(db, publisher, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Shutdown — exit cleanly
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox processor encountered an unexpected error. Retrying in {Interval}s.", Interval.TotalSeconds);
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    // Public for testability — called directly from unit tests with an InMemory context
    public async Task ProcessOnceAsync(MovixDbContext db, IEventPublisher publisher, CancellationToken cancellationToken = default)
    {
        var messages = await db.OutboxMessages
            .Where(x => x.ProcessedAtUtc == null && x.AttemptCount < MaxAttempts)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
            return;

        var nowUtc = DateTime.UtcNow;
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
            }
            catch (Exception ex)
            {
                AttemptCounter.Add(1);
                FailedCounter.Add(1);
                _logger.LogError(
                    ex,
                    "Failed to process outbox message {Id} of type {Type}. Will retry next cycle.",
                    message.Id,
                    message.Type);

                message.RecordFailedAttempt();
                if (message.AttemptCount >= MaxAttempts)
                    DeadLetterCounter.Add(1);
            }
        }

        await db.SaveChangesAsync(cancellationToken); // Single SaveChanges per batch
    }
}
