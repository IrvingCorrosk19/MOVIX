using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Movix.Domain.Entities;
using Movix.Infrastructure.Messaging;
using Movix.Infrastructure.Persistence;
using Xunit;

namespace Movix.Infrastructure.Tests.Messaging;

public class OutboxProcessorTests
{
    private static MovixDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<MovixDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new MovixDbContext(options);
    }

    // F-2 — ProcessOnceAsync marca mensajes pendientes como procesados
    [Fact]
    public async Task ProcessOnceAsync_MarksUnprocessedMessages_AsProcessed()
    {
        var dbName = $"outbox_proc_{Guid.NewGuid()}";
        await using var db = CreateInMemoryContext(dbName);

        var now = DateTime.UtcNow;

        // Insert two pending messages
        db.OutboxMessages.AddRange(
            new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = "UserRegistered",
                Payload = """{"userId":"a1","email":"x@x.com","role":"Passenger"}""",
                CreatedAtUtc = now
            },
            new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = "TripCreated",
                Payload = """{"tripId":"b2"}""",
                CreatedAtUtc = now.AddSeconds(1)
            });

        await db.SaveChangesAsync();

        var publisher = new FakeEventPublisher();
        var processor = new OutboxProcessor(null!, NullLogger<OutboxProcessor>.Instance);
        await processor.ProcessOnceAsync(db, publisher);

        var messages = await db.OutboxMessages.ToListAsync();

        Assert.All(messages, m => Assert.NotNull(m.ProcessedAtUtc));
    }

    // F-2 — Mensajes ya procesados no se tocan en el siguiente ciclo
    [Fact]
    public async Task ProcessOnceAsync_SkipsAlreadyProcessedMessages()
    {
        var dbName = $"outbox_skip_{Guid.NewGuid()}";
        await using var db = CreateInMemoryContext(dbName);

        var processedAt = DateTime.UtcNow.AddMinutes(-10);
        var alreadyProcessed = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "UserRegistered",
            Payload = "{}",
            CreatedAtUtc = processedAt,
            ProcessedAtUtc = processedAt   // already done
        };

        db.OutboxMessages.Add(alreadyProcessed);
        await db.SaveChangesAsync();

        var publisher = new FakeEventPublisher();
        var processor = new OutboxProcessor(null!, NullLogger<OutboxProcessor>.Instance);
        await processor.ProcessOnceAsync(db, publisher);

        // ProcessedAtUtc must not be altered
        var msg = await db.OutboxMessages.FindAsync(alreadyProcessed.Id);
        Assert.Equal(processedAt, msg!.ProcessedAtUtc);
    }

    [Fact]
    public async Task ProcessOnceAsync_CallsPublishAsync_ForEachMessage()
    {
        var dbName = $"outbox_pub_{Guid.NewGuid()}";
        await using var db = CreateInMemoryContext(dbName);

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "TripCreated",
            Payload = """{"tripId":"t1"}""",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var publisher = new FakeEventPublisher();
        var processor = new OutboxProcessor(null!, NullLogger<OutboxProcessor>.Instance);
        await processor.ProcessOnceAsync(db, publisher);

        var published = publisher.GetPublished();
        Assert.Single(published);
        Assert.Equal("TripCreated", published[0].Type);
        Assert.Equal("""{"tripId":"t1"}""", published[0].Payload);
    }

    [Fact]
    public async Task ProcessOnceAsync_WhenMaxAttemptsReached_DoesNotRetry()
    {
        var dbName = $"outbox_max_{Guid.NewGuid()}";
        await using var db = CreateInMemoryContext(dbName);

        var msg = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "TripCreated",
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow
        };
        msg.RecordFailedAttempt();
        msg.RecordFailedAttempt();
        msg.RecordFailedAttempt();
        msg.RecordFailedAttempt();
        msg.RecordFailedAttempt();
        db.OutboxMessages.Add(msg);
        await db.SaveChangesAsync();

        var messageAfterAdd = await db.OutboxMessages.FindAsync(msg.Id);
        Assert.NotNull(messageAfterAdd);
        Assert.Equal(5, messageAfterAdd.AttemptCount);

        var successPublisher = new FakeEventPublisher();
        var processor = new OutboxProcessor(null!, NullLogger<OutboxProcessor>.Instance);
        await processor.ProcessOnceAsync(db, successPublisher);

        var published = successPublisher.GetPublished();
        Assert.Empty(published);
        var messageAfterRun = await db.OutboxMessages.FindAsync(msg.Id);
        Assert.Null(messageAfterRun!.ProcessedAtUtc);
        Assert.Equal(5, messageAfterRun.AttemptCount);
    }

    private sealed class ThrowingEventPublisher : IEventPublisher
    {
        public Task PublishAsync(string type, string payload, CancellationToken ct) => throw new InvalidOperationException("Simulated failure");
    }
}
