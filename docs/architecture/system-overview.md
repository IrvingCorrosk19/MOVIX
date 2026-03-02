# MOVIX System Overview (Technical)

This document describes the current architecture as implemented in code. No global query filters are used for tenant isolation; scoping is explicit in queries and handlers.

---

## Multi-tenant model

- **Tenants:** Table `tenants` (Id, Name, IsActive, RowVersion). Valid tenant required for most authenticated operations.
- **Entities with TenantId:** User (NOT NULL), Driver (NOT NULL), Trip (nullable, set at create or later), TariffPlan (NOT NULL), AuditLog (NOT NULL). Payment has no TenantId; tenant is inferred via Trip.
- **Registration:** Passengers register with a TenantId; driver onboarding associates the driver with the tenant of the current user (JWT). Trips get TenantId from context at creation when RequireTenant is used.

---

## JWT and TenantMiddleware

- **JWT:** Issued on login; contains `tenant_id` (Guid) and role (e.g. Admin, Support, Driver, Passenger, SuperAdmin). Primary source of tenant for authenticated requests.
- **TenantMiddleware** runs after authentication:
  1. Reads `tenant_id` from JWT and role from claims.
  2. If role is SuperAdmin, optional header `X-Tenant-Id` can override the JWT tenant; if the header value is not a valid Guid, `TenantInvalidKey` is set and downstream RequireTenant returns 400 TENANT_INVALID.
  3. If tenant Id is present, middleware validates it against the database: tenant must exist and be active. If not found -> 404 TENANT_NOT_FOUND; if inactive -> 403 TENANT_INACTIVE.
  4. Sets `HttpContext.Items[ItemKey]` = tenant Id and `Items[IsSuperAdminKey]` = true when SuperAdmin.
- **ITenantContext** (implemented by TenantContextService) reads these items. So tenant resolution order: JWT `tenant_id` first; for SuperAdmin, optional `X-Tenant-Id` override; then DB validation.

---

## RequireTenant and tenant scoping strategy

- **RequireTenantAttribute:** Async action filter. If `TenantInvalidKey` is set -> 400 TENANT_INVALID. If `ItemKey` (TenantId) is null -> 400 TENANT_REQUIRED. Applied per action or controller; when applied, the handler can assume `ITenantContext.TenantId` is set (for non–SuperAdmin, already validated by middleware).
- **No global HasQueryFilter:** Tenant filtering is explicit:
  - **Admin lists:** GetAdminTripsQueryHandler and GetAdminDriversQueryHandler use `ApplyTenantScope(query, tenantContext)` on the repository query. If not SuperAdmin and TenantId is null, the extension throws TenantRequiredException (TENANT_REQUIRED).
  - **Single-entity access:** Handlers (e.g. GetTrip, AssignDriver, CreatePayment) compare `trip.TenantId` or `plan.TenantId` with `ITenantContext.TenantId` (or allow SuperAdmin bypass) and return FORBIDDEN/TRIP_NOT_FOUND when cross-tenant.
- **AssignDriver:** Requires Admin/Support and RequireTenant; trip must belong to context tenant (unless SuperAdmin); assigned driver must belong to same tenant as trip.
- **CreatePayment:** Trip must belong to context tenant (unless SuperAdmin); passenger must be current user.

---

## Tariff selection algorithm

- **Multiple active plans per tenant:** A tenant can have several TariffPlans with IsActive = true. Selection is deterministic.
- **TariffPlanRepository.GetApplicableTariffAsync(tenantId, atUtc):**
  - Filter: `TenantId == tenantId`, `IsActive == true`, `(EffectiveFromUtc IS NULL OR EffectiveFromUtc <= atUtc)`, `(EffectiveUntilUtc IS NULL OR EffectiveUntilUtc > atUtc)`.
  - Order: `Priority ASC`, then `EffectiveFromUtc DESC NULLS LAST`.
  - Returns the first record (single plan). Used by QuoteFareQueryHandler and TransitionTripCommandHandler (Complete) with the same atUtc (DateTime.UtcNow / handler time).
- **ActivateTariffPlan:** Does not deactivate other plans. Before activating, `HasOverlappingSamePriorityAsync` checks for another active plan with the same Priority and overlapping effective window; if so, returns TARIFF_PRIORITY_CONFLICT.

---

## Outbox multi-pod safety

- **OutboxProcessor.ProcessOnceAsync:** For PostgreSQL (`db.Database.IsNpgsql()`):
  - Opens an explicit transaction.
  - Selects pending rows with raw SQL: `SELECT ... FROM outbox_messages WHERE ProcessedAtUtc IS NULL AND IsDeadLetter = false AND AttemptCount < 5 ORDER BY CreatedAtUtc LIMIT n FOR UPDATE SKIP LOCKED`.
  - Processes the batch (publish), updates ProcessedAtUtc/AttemptCount/Error, SaveChanges, commits. So each message is locked and updated in one transaction; other pods skip locked rows.
- **Fallback:** For non-Npgsql (e.g. InMemory tests), uses LINQ without FOR UPDATE; no row-level locking.

---

## Audit logging

- **IAuditService.LogAsync(action, entityType, entityId, metadata):** Implemented by AuditService. Builds AuditLog (TenantId from ITenantContext or Guid.Empty, UserId and Role from ICurrentUserService, CreatedAtUtc from IDateTimeService, Metadata serialized with System.Text.Json). Inserts into DbSet and calls SaveChangesAsync (no UnitOfWork). Used only after successful operations.
- **Call sites:** ActivateTariffPlanCommandHandler ("ActivateTariffPlan", "TariffPlan", plan.Id, { Priority }); AssignDriverCommandHandler ("AssignDriver", "Trip", trip.Id, { driverId }); AcceptTripCommandHandler ("AcceptTrip", "Trip", trip.Id, null); CreatePaymentCommandHandler ("CreatePayment", "Trip", trip.Id, { amount }). If audit fails, the request fails (no try/catch around audit).

---

## Security enforcement per role

- **Controllers:** [Authorize] and [Authorize(Roles = "Admin,Support")] or "Driver,Admin" etc. on class or action.
- **Handlers:** Additional checks: AssignDriver requires Admin/Support/SuperAdmin; AcceptTrip requires driver to be the current user and trip.TenantId == driver.TenantId; GetTrip allows owner (passenger/driver) or Admin/Support/SuperAdmin, with tenant scoping for admin path; CreatePayment requires trip.PassengerId == userId and tenant match. FORBIDDEN or TRIP_NOT_FOUND returned when cross-tenant or wrong role.
- **Admin list endpoints:** GetAdminTrips and GetAdminDrivers apply ApplyTenantScope so only the context tenant’s data is returned (SuperAdmin sees all). No RequireTenant on AdminController class; tenant comes from JWT/middleware and is enforced in the query layer.
