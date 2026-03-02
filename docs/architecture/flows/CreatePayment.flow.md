# Flow: CreatePayment

1. **HTTP**  
   POST `/api/v1/payments`. Headers: `Idempotency-Key` (required). Body: `CreatePaymentRequest { TripId, Amount, Currency? }`.

2. **Controller**  
   - `[Authorize]`, no RequireTenant.  
   - If Idempotency-Key missing, return 400 IDEMPOTENCY_KEY_REQUIRED.  
   - Sends `CreatePaymentCommand(idempotencyKey, request.TripId, request.Amount, request.Currency ?? "USD")`.  
   - Maps: PAYMENT_GATEWAY_ERROR -> 503, FORBIDDEN -> 403, rest -> 400.

3. **Handler (CreatePaymentCommandHandler)**  
   - **Auth:** UserId null -> UNAUTHORIZED.  
   - **Idempotency:** `IIdempotencyService.GetResponseAsync(idempotencyKey)`. If existing and payment exists, return cached PaymentDto (success).  
   - **Trip:** `ITripRepository.GetByIdAsync(request.TripId)`. Null -> TRIP_NOT_FOUND. Status must be Completed -> TRIP_NOT_COMPLETED. trip.PassengerId must equal userId -> UNAUTHORIZED_PAYMENT.  
   - **Tenant:** If not SuperAdmin and trip.TenantId != TenantContext.TenantId -> FORBIDDEN.  
   - **Payment entity:** New Payment (Pending), `IPaymentRepository.AddAsync`.  
   - **Gateway:** `IPaymentGateway.CreatePaymentIntentAsync(...)`. On exception -> PAYMENT_GATEWAY_ERROR.  
   - **Outbox:** Add OutboxMessage Type = "PaymentCreated" with payload.  
   - **Persistence:** `IUnitOfWork.SaveChangesAsync()`, then `IIdempotencyService.StoreAsync(idempotencyKey, paymentEntity.Id)`.  
   - **Audit:** `IAuditService.LogAsync("CreatePayment", "Trip", trip.Id, new { amount = paymentEntity.Amount })`.  
   - Return `Result.Success(Map(paymentEntity, clientSecret))`.

4. **DB impact**  
   - `payments`: one new row.  
   - `outbox_messages`: one new row.  
   - Redis (or idempotency store): store idempotency key -> payment id.  
   - `audit_logs`: one new row.

5. **Response**  
   - 200 with PaymentDto (Id, TripId, Amount, Currency, Status, CreatedAtUtc, ClientSecret).  
   - 503 PAYMENT_GATEWAY_ERROR, 403 FORBIDDEN, 400 for others.
