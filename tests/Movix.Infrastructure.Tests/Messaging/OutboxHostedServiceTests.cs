using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Movix.Domain.Entities;
using Movix.Infrastructure.Messaging;
using Movix.Infrastructure.Persistence;
using Xunit;

namespace Movix.Infrastructure.Tests.Messaging;

public class OutboxHostedServiceTests
{
    [Fact]
    public async Task HostedService_WhenEnabled_ProcessesPendingMessages()
    {
        var dbName = "outbox_hosted_" + Guid.NewGuid();
        var optionsBuilder = new DbContextOptionsBuilder<MovixDbContext>()
            .UseInMemoryDatabase(dbName);
        await using (var seed = new MovixDbContext(optionsBuilder.Options))
        {
            seed.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = "TestEvent",
                Payload = "{}",
                CreatedAtUtc = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        var publisher = new FakeEventPublisher();
        var services = new ServiceCollection();
        services.AddDbContext<MovixDbContext>(opts => opts.UseInMemoryDatabase(dbName));
        services.AddSingleton<IEventPublisher>(publisher);
        services.AddSingleton(Options.Create(new OutboxOptions { Enabled = true, PollIntervalMs = 100, MaxBatchSize = 50 }));
        services.AddLogging();
        services.AddScoped<OutboxProcessor>();
        var sp = services.BuildServiceProvider();
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MovixDbContext>();
            var pub = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
            var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
            await processor.ProcessOnceAsync(db, pub, default);
        }

        Assert.Single(publisher.GetPublished());
        Assert.Equal("TestEvent", publisher.GetPublished()[0].Type);

        await using (var check = new MovixDbContext(optionsBuilder.Options))
        {
            var msg = await check.OutboxMessages.FirstAsync();
            Assert.NotNull(msg.ProcessedAtUtc);
        }
    }

    [Fact]
    public async Task HostedService_DoesNotProcessDeadLetter()
    {
        var dbName = "outbox_hosted_dl_" + Guid.NewGuid();
        var optionsBuilder = new DbContextOptionsBuilder<MovixDbContext>()
            .UseInMemoryDatabase(dbName);
        await using (var seed = new MovixDbContext(optionsBuilder.Options))
        {
            var msg = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = "DeadEvent",
                Payload = "{}",
                CreatedAtUtc = DateTime.UtcNow
            };
            seed.OutboxMessages.Add(msg);
            await seed.SaveChangesAsync();
            var loaded = await seed.OutboxMessages.FindAsync(msg.Id);
            loaded!.MarkAsDeadLetter();
            await seed.SaveChangesAsync();
        }

        var publisher = new FakeEventPublisher();
        var services = new ServiceCollection();
        services.AddDbContext<MovixDbContext>(opts => opts.UseInMemoryDatabase(dbName));
        services.AddSingleton<IEventPublisher>(publisher);
        services.AddSingleton(Options.Create(new OutboxOptions { Enabled = true, MaxBatchSize = 50 }));
        services.AddLogging();
        services.AddScoped<OutboxProcessor>();
        var sp = services.BuildServiceProvider();
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MovixDbContext>();
            var pub = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
            var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
            await processor.ProcessOnceAsync(db, pub, default);
        }

        Assert.Empty(publisher.GetPublished());
    }
}
