using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Movix.Infrastructure.Persistence;

namespace Movix.Infrastructure.Messaging;

public sealed class OutboxHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxHostedService> _logger;

    public OutboxHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxOptions> options,
        ILogger<OutboxHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_options.Enabled)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1000), stoppingToken);
                continue;
            }

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<MovixDbContext>();
                var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
                var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
                await processor.ProcessOnceAsync(db, publisher, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                var jitter = _options.JitterMaxMs > 0 ? Random.Shared.Next(0, _options.JitterMaxMs) : 0;
                var backoffMs = _options.ErrorBackoffMs + jitter;
                _logger.LogWarning(ex,
                    "Outbox worker unexpected error. Backing off {BackoffMs}ms.",
                    backoffMs);
                await Task.Delay(TimeSpan.FromMilliseconds(backoffMs), stoppingToken);
                continue;
            }

            var interval = Math.Max(0, _options.PollIntervalMs);
            await Task.Delay(TimeSpan.FromMilliseconds(interval), stoppingToken);
        }
    }
}
