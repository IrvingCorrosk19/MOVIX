using Microsoft.EntityFrameworkCore;
using Movix.Application.Common.Exceptions;
using Movix.Application.Common.Interfaces;

namespace Movix.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly MovixDbContext _db;

    public UnitOfWork(MovixDbContext db)
    {
        _db = db;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException();
        }
    }
}
