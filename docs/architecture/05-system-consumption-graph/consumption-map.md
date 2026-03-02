# System Consumption Graph

Dependencies between operations as documented in endpoint and flow docs. Represented as hierarchical text (no ASCII art). Order reflects typical or logical flow; not all paths are sequential.

---

## Trip and payment lifecycle

Auth  
  - register, login, refresh, logout.  
  - login yields JWT (tenant_id, role). No downstream consumption of other MOVIX endpoints.

Trips (Create)  
  - POST api/v1/trips. Requires tenant (RequireTenant), idempotency key.  
  - Consumes: ITenantContext, ICurrentUserService, ITripRepository, IIdempotencyService.  
  - Produces: Trip (Requested). No Outbox, no Audit.

AssignDriver  
  - POST api/v1/trips/{id}/assign-driver. Admin/Support, RequireTenant.  
  - Consumes: Trip (Requested), DriverAvailability, ITenantContext, ITripRepository, IDriverAvailabilityRepository.  
  - Produces: Trip (Accepted), OutboxMessage DriverAssigned, AuditLog AssignDriver.

Accept  
  - POST api/v1/trips/{id}/accept. Driver/Admin.  
  - Consumes: Trip (Accepted from assign or same state), Driver, Vehicle, ITripRepository, IDriverRepository.  
  - Produces: Trip (Accepted with DriverId/VehicleId), AuditLog AcceptTrip. No Outbox.

Start  
  - POST api/v1/trips/{id}/start. Driver/Admin, RequireTenant.  
  - Consumes: Trip (e.g. Arrived), TransitionTripCommandHandler.  
  - Produces: Trip (InProgress). No Audit, no Outbox in flow docs.

Complete  
  - POST api/v1/trips/{id}/complete. Driver/Admin.  
  - Consumes: Trip (InProgress), ITariffPlanRepository.GetApplicableTariffAsync (for fare snapshot), IFareCalculator, IDriverAvailabilityRepository.  
  - Produces: Trip (Completed, FinalAmount/tariff snapshot), OutboxMessage TripCompleted, driver availability cleared.

CreatePayment  
  - POST api/v1/payments. Requires TripId, idempotency key.  
  - Consumes: Trip (Completed), ITripRepository, IPaymentGateway, IPaymentRepository, IIdempotencyService.  
  - Produces: Payment (Pending), OutboxMessage PaymentCreated, AuditLog CreatePayment.

Webhook  
  - POST api/v1/payments/webhook. AllowAnonymous.  
  - Consumes: Stripe payload, signature.  
  - Produces: Payment status update (e.g. Confirmed). Idempotent by event id. May trigger downstream processing.

Outbox processing  
  - Background OutboxProcessor.  
  - Consumes: OutboxMessage (DriverAssigned, TripCompleted, PaymentCreated, etc.).  
  - Produces: Published events, ProcessedAtUtc updated. PostgreSQL: SELECT FOR UPDATE SKIP LOCKED in transaction.  
  - No direct consumption of other MOVIX HTTP endpoints; consumes DB outbox_messages.

---

## Admin tariffs and fare flow

Admin Tariffs  
  - POST api/v1/admin/tariffs (create), PUT api/v1/admin/tariffs/{id} (update). Admin/Support, RequireTenant.  
  - Consumes: ITariffPlanRepository, ITenantContext.  
  - Produces: TariffPlan (IsActive false on create). No Audit on create/update.

Activate  
  - POST api/v1/admin/tariffs/{id}/activate. Admin/Support, RequireTenant.  
  - Consumes: TariffPlan, ITariffPlanRepository (GetByIdAsync, HasOverlappingSamePriorityAsync).  
  - Produces: TariffPlan IsActive = true, AuditLog ActivateTariffPlan.

QuoteFare  
  - GET api/v1/admin/tariffs/fare/quote or GET api/v1/fare/quote.  
  - Consumes: ITenantContext (tenantId), ITariffPlanRepository.GetApplicableTariffAsync(tenantId, atUtc), IDateTimeService, IFareCalculator.  
  - Produces: QuoteFareDto (read-only). No DB writes, no Audit, no Outbox.

TransitionTrip (Complete)  
  - POST api/v1/trips/{id}/complete.  
  - Consumes: GetApplicableTariffAsync (same algorithm as QuoteFare), IFareCalculator for FinalAmount snapshot.  
  - As above: produces Trip Completed, TripCompleted outbox, no Audit.

---

## Summary hierarchy (key flows)

- Auth -> Trips Create -> AssignDriver -> Accept -> (Arrive ->) Start -> Complete -> CreatePayment -> Webhook -> Outbox processing.
- Admin Tariffs (create/update) -> Activate -> (used by) QuoteFare and TransitionTrip Complete (GetApplicableTariffAsync).
