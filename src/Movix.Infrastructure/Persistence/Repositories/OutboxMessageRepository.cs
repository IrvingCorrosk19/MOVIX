using Microsoft.EntityFrameworkCore;
using Movix.Application.Outbox;
using Movix.Domain.Entities;
using Movix.Infrastructure.Persistence;

namespace Movix.Infrastructure.Persistence.Repositories;

public class OutboxMessageRepository : IOutboxMessageRepository
{
    private readonly MovixDbContext _db;

    public OutboxMessageRepository(MovixDbContext db)
    {
        _db = db;
    }

    public async Task<OutboxMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.OutboxMessages.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        _db.OutboxMessages.Add(message);
        return Task.CompletedTask;
    }
}
