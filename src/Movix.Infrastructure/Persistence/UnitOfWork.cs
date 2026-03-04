using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Movix.Application.Common.Exceptions;
using Movix.Application.Common.Interfaces;

namespace Movix.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly MovixDbContext _db;
    private readonly ILogger<UnitOfWork>? _logger;

    public UnitOfWork(MovixDbContext db, ILogger<UnitOfWork>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Diagnostic: log which entities had 0 rows affected (root cause of 409).
            var sb = new StringBuilder();
            foreach (var entry in ex.Entries)
                sb.Append($"[{entry.Metadata.Name}] State={entry.State} ");
            _logger?.LogWarning("Concurrency conflict. Failed entries: {Entries}", sb.ToString());
            throw new ConcurrencyException("Concurrency conflict.", ex);
        }
    }
}
