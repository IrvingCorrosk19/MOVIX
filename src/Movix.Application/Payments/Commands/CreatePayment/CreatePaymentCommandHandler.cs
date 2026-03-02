using MediatR;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;
using Movix.Application.Outbox;
using Movix.Application.Payments;
using Movix.Application.Trips;
using Movix.Domain.Entities;
using Movix.Domain.Enums;

namespace Movix.Application.Payments.Commands.CreatePayment;

public class CreatePaymentCommandHandler : IRequestHandler<CreatePaymentCommand, Result<PaymentDto>>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly ITripRepository _tripRepository;
    private readonly IIdempotencyService _idempotencyService;
    private readonly IOutboxMessageRepository _outboxRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly ICurrentUserService _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly IDateTimeService _dateTime;
    private readonly IUnitOfWork _uow;

    public CreatePaymentCommandHandler(
        IPaymentRepository paymentRepository,
        ITripRepository tripRepository,
        IIdempotencyService idempotencyService,
        IOutboxMessageRepository outboxRepository,
        IPaymentGateway paymentGateway,
        ICurrentUserService currentUser,
        ITenantContext tenantContext,
        IDateTimeService dateTime,
        IUnitOfWork uow)
    {
        _paymentRepository = paymentRepository;
        _tripRepository = tripRepository;
        _idempotencyService = idempotencyService;
        _outboxRepository = outboxRepository;
        _paymentGateway = paymentGateway;
        _currentUser = currentUser;
        _tenantContext = tenantContext;
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
                return Result<PaymentDto>.Success(Map(payment, null));
        }

        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null)
            return Result<PaymentDto>.Failure("Trip not found", "TRIP_NOT_FOUND");
        if (trip.Status != TripStatus.Completed)
            return Result<PaymentDto>.Failure("Trip not completed", "TRIP_NOT_COMPLETED");
        if (trip.PassengerId != userId.Value)
            return Result<PaymentDto>.Failure("Unauthorized payment attempt", "UNAUTHORIZED_PAYMENT");

        if (!_tenantContext.IsSuperAdmin && trip.TenantId.HasValue && trip.TenantId != _tenantContext.TenantId)
            return Result<PaymentDto>.Failure("Forbidden", "FORBIDDEN");

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

        CreatePaymentIntentResult gatewayResult;
        try
        {
            gatewayResult = await _paymentGateway.CreatePaymentIntentAsync(new CreatePaymentIntentRequest(
                request.Amount,
                request.Currency,
                paymentEntity.Id,
                request.TripId,
                userId.Value,
                null), cancellationToken);
        }
        catch (Exception)
        {
            return Result<PaymentDto>.Failure("Payment gateway error", "PAYMENT_GATEWAY_ERROR");
        }

        paymentEntity.ExternalPaymentId = gatewayResult.ExternalPaymentId;
        var payload = "{\"paymentId\":\"" + paymentEntity.Id + "\",\"tripId\":\"" + request.TripId + "\",\"amount\":" + request.Amount + ",\"currency\":\"" + (request.Currency ?? "USD") + "\",\"occurredAtUtc\":\"" + now.ToString("O") + "\"}";
        await _outboxRepository.AddAsync(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "PaymentCreated",
            Payload = payload,
            CreatedAtUtc = now
        }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        await _idempotencyService.StoreAsync(request.IdempotencyKey, paymentEntity.Id.ToString(), cancellationToken);

        return Result<PaymentDto>.Success(Map(paymentEntity, gatewayResult.ClientSecret));
    }

    private static PaymentDto Map(Payment p, string? clientSecret) => new(
        p.Id,
        p.TripId,
        p.Amount,
        p.Currency,
        p.Status.ToString(),
        p.CreatedAtUtc,
        clientSecret);
}
