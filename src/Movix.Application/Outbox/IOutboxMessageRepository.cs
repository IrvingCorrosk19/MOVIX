using Movix.Domain.Entities;

namespace Movix.Application.Outbox;

public interface IOutboxMessageRepository
{
    Task<OutboxMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
