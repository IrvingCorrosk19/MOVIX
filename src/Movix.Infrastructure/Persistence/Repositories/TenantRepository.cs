using Microsoft.EntityFrameworkCore;
using Movix.Application.Tenants;
using Movix.Domain.Entities;
using Movix.Infrastructure.Persistence;

namespace Movix.Infrastructure.Persistence.Repositories;

public class TenantRepository : ITenantRepository
{
    private readonly MovixDbContext _db;

    public TenantRepository(MovixDbContext db)
    {
        _db = db;
    }

    public async Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.Tenants.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<IReadOnlyList<Tenant>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Tenants.OrderBy(t => t.Name).ToListAsync(cancellationToken);
    }

    public Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        _db.Tenants.Add(tenant);
        return Task.CompletedTask;
    }
}
