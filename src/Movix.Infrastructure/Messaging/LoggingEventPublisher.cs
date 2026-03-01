using Microsoft.Extensions.Logging;

namespace Movix.Infrastructure.Messaging;

public sealed class LoggingEventPublisher : IEventPublisher
{
    private readonly ILogger<LoggingEventPublisher> _logger;

    public LoggingEventPublisher(ILogger<LoggingEventPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(Guid eventId, string type, string payload, string? correlationId, CancellationToken ct)
    {
        _logger.LogInformation("Publishing event {EventId} {Type} Correlation:{CorrelationId}", eventId, type, correlationId);
        return Task.CompletedTask;
    }
}
