using Movix.Infrastructure.Messaging;

namespace Movix.Infrastructure.Tests.Messaging;

public sealed class FakeEventPublisher : IEventPublisher
{
    private readonly List<(string Type, string Payload)> _published = new();

    public Task PublishAsync(string type, string payload, CancellationToken ct)
    {
        _published.Add((type, payload));
        return Task.CompletedTask;
    }

    public IReadOnlyList<(string Type, string Payload)> GetPublished() => _published;
}
