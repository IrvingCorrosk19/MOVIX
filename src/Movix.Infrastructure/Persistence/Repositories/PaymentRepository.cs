using Microsoft.EntityFrameworkCore;
using Movix.Application.Payments;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Movix.Infrastructure.Persistence;

namespace Movix.Infrastructure.Persistence.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly MovixDbContext _db;

    public PaymentRepository(MovixDbContext db)
    {
        _db = db;
    }

    public async Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.Payments.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<Payment?> GetByExternalPaymentIdAsync(string externalPaymentId, CancellationToken cancellationToken = default)
    {
        return await _db.Payments.FirstOrDefaultAsync(p => p.ExternalPaymentId == externalPaymentId, cancellationToken);
    }

    public async Task<IReadOnlyList<Payment>> GetFilteredAsync(Guid? tenantId, PaymentStatus? status, DateTime? from, DateTime? to, Guid? tripId, CancellationToken cancellationToken = default)
    {
        var query = _db.Payments.Include(p => p.Trip).AsNoTracking();
        if (tenantId.HasValue)
            query = query.Where(p => p.Trip != null && p.Trip.TenantId == tenantId.Value);
        if (status.HasValue)
            query = query.Where(p => p.Status == status.Value);
        if (from.HasValue)
            query = query.Where(p => p.CreatedAtUtc >= from.Value);
        if (to.HasValue)
            query = query.Where(p => p.CreatedAtUtc <= to.Value);
        if (tripId.HasValue)
            query = query.Where(p => p.TripId == tripId.Value);
        return await query.OrderByDescending(p => p.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public Task AddAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        _db.Payments.Add(payment);
        return Task.CompletedTask;
    }
}
