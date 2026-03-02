# Flow: CreatePayment

**Layer 1: HTTP Layer**  
POST `/api/v1/payments`. Header `Idempotency-Key` required. Body: CreatePaymentRequest (TripId, Amount, Currency). Rate limiting: payments policy.

**Layer 2: Controller**  
PaymentsController.Create. [Authorize]. No RequireTenant. If Idempotency-Key missing returns 400 IDEMPOTENCY_KEY_REQUIRED. Sends CreatePaymentCommand(idempotencyKey, request.TripId, request.Amount, request.Currency ?? "USD"). Maps: PAYMENT_GATEWAY_ERROR -> 503, FORBIDDEN -> 403; rest -> 400. Success -> Ok(result.Data).

**Layer 3: Command/Query**  
CreatePaymentCommand(IdempotencyKey, TripId, Amount, Currency). IRequest<Result<PaymentDto>>.

**Layer 4: Handler**  
CreatePaymentCommandHandler. UserId null -> UNAUTHORIZED. Idempotency: GetResponseAsync(idempotencyKey); if value exists and payment exists for that id, return Map(payment, null) success. Load trip; null -> TRIP_NOT_FOUND. Trip.Status must be Completed -> TRIP_NOT_COMPLETED. trip.PassengerId must equal userId -> UNAUTHORIZED_PAYMENT. If not SuperAdmin and trip.TenantId != TenantContext.TenantId -> FORBIDDEN. Create Payment entity (Pending), AddAsync. Call IPaymentGateway.CreatePaymentIntentAsync; on exception -> PAYMENT_GATEWAY_ERROR. Set payment.ExternalPaymentId from result. Add OutboxMessage Type "PaymentCreated" with payload. SaveChangesAsync. StoreAsync(idempotencyKey, payment.Id). IAuditService.LogAsync("CreatePayment", "Trip", trip.Id, new { amount = paymentEntity.Amount }). Return Map(paymentEntity, clientSecret).

**Layer 5: Domain Rules**  
Payment created only for completed trip. Payer must be trip passenger. Tenant of trip must match context (or SuperAdmin). Payment in Pending until webhook/confirmation.

**Layer 6: Tenant Enforcement**  
No RequireTenant on controller. Handler compares trip.TenantId with ITenantContext.TenantId; mismatch and not SuperAdmin -> FORBIDDEN.

**Layer 7: Repository/DB**  
ITripRepository.GetByIdAsync. IPaymentRepository.AddAsync. IUnitOfWork.SaveChangesAsync. IIdempotencyService.GetResponseAsync / StoreAsync. Tables: payments (insert), outbox_messages (insert). Idempotency store (e.g. Redis) key -> payment Id.

**Layer 8: Outbox**  
IOutboxMessageRepository.AddAsync(OutboxMessage Type = "PaymentCreated", Payload). Same transaction as SaveChangesAsync.

**Layer 9: Audit**  
IAuditService.LogAsync("CreatePayment", "Trip", trip.Id, new { amount = paymentEntity.Amount }). After SaveChangesAsync and StoreAsync.

**Layer 10: Response mapping**  
200 with PaymentDto (Id, TripId, Amount, Currency, Status, CreatedAtUtc, ClientSecret). 503 PAYMENT_GATEWAY_ERROR, 403 FORBIDDEN, 400 for IDEMPOTENCY_KEY_REQUIRED, UNAUTHORIZED, TRIP_NOT_FOUND, TRIP_NOT_COMPLETED, UNAUTHORIZED_PAYMENT, others.
