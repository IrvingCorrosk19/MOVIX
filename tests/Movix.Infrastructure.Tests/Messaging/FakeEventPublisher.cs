using Movix.Infrastructure.Messaging;

namespace Movix.Infrastructure.Tests.Messaging;

public sealed class FakeEventPublisher : IEventPublisher
{
    private readonly List<(Guid EventId, string Type, string Payload, string? CorrelationId)> _published = new();

    public Task PublishAsync(Guid eventId, string type, string payload, string? correlationId, CancellationToken ct)
    {
        _published.Add((eventId, type, payload, correlationId));
        return Task.CompletedTask;
    }

    public IReadOnlyList<(Guid EventId, string Type, string Payload, string? CorrelationId)> GetPublished() => _published;
}
