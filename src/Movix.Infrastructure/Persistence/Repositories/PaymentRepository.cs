using Movix.Application.Payments;
using Movix.Domain.Entities;

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

    public Task AddAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        _db.Payments.Add(payment);
        return Task.CompletedTask;
    }
}
