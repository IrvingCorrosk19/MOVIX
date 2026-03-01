namespace Movix.Infrastructure.Messaging;

public interface IEventPublisher
{
    Task PublishAsync(string type, string payload, CancellationToken ct);
}
