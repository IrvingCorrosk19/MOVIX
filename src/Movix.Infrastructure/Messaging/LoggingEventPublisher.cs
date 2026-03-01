using Microsoft.Extensions.Logging;

namespace Movix.Infrastructure.Messaging;

public sealed class LoggingEventPublisher : IEventPublisher
{
    private readonly ILogger<LoggingEventPublisher> _logger;

    public LoggingEventPublisher(ILogger<LoggingEventPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(string type, string payload, CancellationToken ct)
    {
        _logger.LogInformation("Publishing event {Type} {Payload}", type, payload);
        return Task.CompletedTask;
    }
}
