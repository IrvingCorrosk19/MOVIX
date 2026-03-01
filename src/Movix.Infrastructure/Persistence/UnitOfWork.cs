using Movix.Application.Common.Interfaces;

namespace Movix.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly MovixDbContext _db;

    public UnitOfWork(MovixDbContext db)
    {
        _db = db;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
