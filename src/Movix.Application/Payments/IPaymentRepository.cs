using Movix.Domain.Entities;
using Movix.Domain.Enums;

namespace Movix.Application.Payments;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Payment?> GetByExternalPaymentIdAsync(string externalPaymentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Payment>> GetFilteredAsync(Guid? tenantId, PaymentStatus? status, DateTime? from, DateTime? to, Guid? tripId, CancellationToken cancellationToken = default);
    Task AddAsync(Payment payment, CancellationToken cancellationToken = default);
}
