using MediatR;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;
using Movix.Domain.Entities;
using Movix.Domain.Enums;

namespace Movix.Application.Payments.Commands.CreatePayment;

public class CreatePaymentCommandHandler : IRequestHandler<CreatePaymentCommand, Result<PaymentDto>>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IIdempotencyService _idempotencyService;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeService _dateTime;
    private readonly IUnitOfWork _uow;

    public CreatePaymentCommandHandler(
        IPaymentRepository paymentRepository,
        IIdempotencyService idempotencyService,
        ICurrentUserService currentUser,
        IDateTimeService dateTime,
        IUnitOfWork uow)
    {
        _paymentRepository = paymentRepository;
        _idempotencyService = idempotencyService;
        _currentUser = currentUser;
        _dateTime = dateTime;
        _uow = uow;
    }

    public async Task<Result<PaymentDto>> Handle(CreatePaymentCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue)
            return Result<PaymentDto>.Failure("Unauthorized", "UNAUTHORIZED");

        var existing = await _idempotencyService.GetResponseAsync(request.IdempotencyKey, cancellationToken);
        if (existing != null)
        {
            var existingId = Guid.Parse(existing);
            var payment = await _paymentRepository.GetByIdAsync(existingId, cancellationToken);
            if (payment != null)
                return Result<PaymentDto>.Success(Map(payment));
        }

        var now = _dateTime.UtcNow;
        var paymentEntity = new Payment
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = request.IdempotencyKey,
            TripId = request.TripId,
            PayerId = userId.Value,
            Amount = request.Amount,
            Currency = request.Currency,
            Status = PaymentStatus.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = userId.ToString(),
            UpdatedBy = userId.ToString(),
            RowVersion = new byte[] { 1 }
        };

        await _paymentRepository.AddAsync(paymentEntity, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        await _idempotencyService.StoreAsync(request.IdempotencyKey, paymentEntity.Id.ToString(), cancellationToken);

        return Result<PaymentDto>.Success(Map(paymentEntity));
    }

    private static PaymentDto Map(Payment p) => new(
        p.Id,
        p.TripId,
        p.Amount,
        p.Currency,
        p.Status.ToString(),
        p.CreatedAtUtc);
}
