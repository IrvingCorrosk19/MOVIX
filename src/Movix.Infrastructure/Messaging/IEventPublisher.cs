namespace Movix.Infrastructure.Messaging;

public interface IEventPublisher
{
    Task PublishAsync(Guid eventId, string type, string payload, string? correlationId, CancellationToken ct);
}
