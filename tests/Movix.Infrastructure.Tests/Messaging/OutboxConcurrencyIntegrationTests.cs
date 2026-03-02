using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Movix.Domain.Entities;
using Movix.Infrastructure.Messaging;
using Movix.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Movix.Infrastructure.Tests.Messaging;

/// <summary>
/// Integration tests for outbox multi-worker safety using real PostgreSQL (Testcontainers).
/// </summary>
public sealed class OutboxConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private MovixDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MovixDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npgsql =>
            {
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public");
                npgsql.EnableRetryOnFailure(2);
            })
            .Options;
        return new MovixDbContext(options);
    }

    private static async Task ApplyMigrationsAsync(MovixDbContext db)
    {
        await db.Database.MigrateAsync();
    }

    /// <summary>Requires Docker. Run with: dotnet test --filter "Category=OutboxIntegration"</summary>
    [Fact]
    [Trait("Category", "OutboxIntegration")]
    public async Task Outbox_WhenTwoWorkersRun_ProcessesEachMessageOnce()
    {
        await using var seedDb = CreateDbContext();
        await ApplyMigrationsAsync(seedDb);

        const int messageCount = 10;
        var messageIds = new List<Guid>();
        for (var i = 0; i < messageCount; i++)
        {
            var msg = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = "TestEvent",
                Payload = $"{{\"i\":{i}}}",
                CreatedAtUtc = DateTime.UtcNow.AddSeconds(-i)
            };
            messageIds.Add(msg.Id);
            seedDb.OutboxMessages.Add(msg);
        }
        await seedDb.SaveChangesAsync();

        var published = new ConcurrentBag<(Guid EventId, string Type, string Payload)>();
        var publisher = new ConcurrentEventPublisher(published);

        var processor = new OutboxProcessor(
            Options.Create(new OutboxOptions { MaxBatchSize = 5 }),
            NullLogger<OutboxProcessor>.Instance);

        async Task Worker()
        {
            for (var i = 0; i < 15; i++)
            {
                if (published.Count >= messageCount) break;
                await using var db = CreateDbContext();
                await processor.ProcessOnceAsync(db, publisher, default);
                await Task.Delay(10);
            }
        }

        await Task.WhenAll(Worker(), Worker());

        Assert.Equal(messageCount, published.Count);
        var distinctEventIds = published.Select(x => x.EventId).Distinct().ToList();
        Assert.Equal(messageCount, distinctEventIds.Count);

        await using var checkDb = CreateDbContext();
        var processed = await checkDb.OutboxMessages
            .Where(x => messageIds.Contains(x.Id))
            .ToListAsync();
        Assert.All(processed, m => Assert.NotNull(m.ProcessedAtUtc));
        Assert.Equal(messageCount, processed.Count);
    }

    /// <summary>Requires Docker. Run with: dotnet test --filter "Category=OutboxIntegration"</summary>
    [Fact]
    [Trait("Category", "OutboxIntegration")]
    public async Task Outbox_WhenWorkerCrashes_BeforeCommit_MessageRemainsUnprocessed()
    {
        await using var seedDb = CreateDbContext();
        await ApplyMigrationsAsync(seedDb);

        var msg = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "FailingEvent",
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow
        };
        seedDb.OutboxMessages.Add(msg);
        await seedDb.SaveChangesAsync();

        var publisher = new ThrowingOnceEventPublisher();
        var processor = new OutboxProcessor(
            Options.Create(new OutboxOptions { MaxBatchSize = 10 }),
            NullLogger<OutboxProcessor>.Instance);

        await using (var db = CreateDbContext())
        {
            await processor.ProcessOnceAsync(db, publisher, default);
        }

        await using var checkDb = CreateDbContext();
        var after = await checkDb.OutboxMessages.FindAsync(msg.Id);
        Assert.NotNull(after);
        Assert.Null(after.ProcessedAtUtc);
        Assert.True(after.AttemptCount >= 1);
    }

    private sealed class ConcurrentEventPublisher : IEventPublisher
    {
        private readonly ConcurrentBag<(Guid EventId, string Type, string Payload)> _published;

        public ConcurrentEventPublisher(ConcurrentBag<(Guid EventId, string Type, string Payload)> published)
        {
            _published = published;
        }

        public Task PublishAsync(Guid eventId, string type, string payload, string? correlationId, CancellationToken ct)
        {
            _published.Add((eventId, type, payload));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingOnceEventPublisher : IEventPublisher
    {
        private int _callCount;

        public Task PublishAsync(Guid eventId, string type, string payload, string? correlationId, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
                throw new InvalidOperationException("Simulated crash before commit");
            return Task.CompletedTask;
        }
    }
}
