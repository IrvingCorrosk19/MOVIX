# Security and Tenant Enforcement Map

Data from existing documentation (system-overview, endpoint contracts, flow docs). No code changes.

---

## JWT and tenant_id

- JWT is issued on login; contains claim `tenant_id` (Guid) and role (Admin, Support, Driver, Passenger, SuperAdmin).
- Primary source of tenant for authenticated requests is JWT `tenant_id`.
- TenantMiddleware runs after authentication: reads `tenant_id` and role from JWT claims.
- If role is SuperAdmin, optional header `X-Tenant-Id` can override JWT tenant; if the header value is not a valid Guid, TenantInvalidKey is set and RequireTenant returns 400 TENANT_INVALID.
- Middleware validates tenant against DB: must exist and be active. Not found -> 404 TENANT_NOT_FOUND; inactive -> 403 TENANT_INACTIVE.
- Middleware sets HttpContext.Items[ItemKey] = tenant Id and Items[IsSuperAdminKey] for SuperAdmin.
- ITenantContext (TenantContextService) reads these items. Resolution order: JWT tenant_id first; for SuperAdmin, optional X-Tenant-Id override; then DB validation.

---

## RequireTenant

- RequireTenantAttribute: async action filter. If TenantInvalidKey set -> 400 TENANT_INVALID. If TenantId (ItemKey) null -> 400 TENANT_REQUIRED.
- Applied per action or per controller. When applied, handler can assume ITenantContext.TenantId is set (for non-SuperAdmin, already validated by middleware).
- Endpoints with RequireTenant Yes (from endpoint docs): POST trips, POST assign-driver, POST arrive, POST start, POST cancel, GET fare/quote, entire TariffsController (admin/tariffs), POST payments not required (tenant enforced in handler).

---

## Where TenantId is validated

- RequireTenant: at controller/filter level before handler runs; returns 400 if tenant missing or invalid.
- TenantMiddleware: validates tenant existence and IsActive before setting context.
- Handlers that compare entity tenant to context:
  - AssignDriverCommandHandler: trip.TenantId == TenantContext.TenantId (unless SuperAdmin); driver.TenantId == trip.TenantId.
  - CreatePaymentCommandHandler: trip.TenantId == TenantContext.TenantId (unless SuperAdmin).
  - GetTripQueryHandler: by role; admin path uses tenant scoping.
  - ActivateTariffPlanCommandHandler: plan.TenantId == request.TenantId (TARIFF_CROSS_TENANT).
  - AcceptTripCommandHandler: trip.TenantId == driver.TenantId (no context tenant; driver is current user).
  - UpdateTariffPlanCommandHandler: plan.TenantId vs context (TARIFF_CROSS_TENANT).

---

## Where ApplyTenantScope is applied

- GetAdminTripsQueryHandler: ApplyTenantScope on the admin trips query; if not SuperAdmin and TenantId null, TenantRequiredException (TENANT_REQUIRED).
- GetAdminDriversQueryHandler: ApplyTenantScope on the admin drivers query; same behavior.
- No global HasQueryFilter; tenant filtering is explicit in these list handlers.

---

## Where explicit trip.TenantId validation occurs

- AssignDriverCommandHandler: trip.TenantId != TenantContext.TenantId -> FORBIDDEN (unless SuperAdmin).
- CreatePaymentCommandHandler: trip.TenantId != TenantContext.TenantId -> FORBIDDEN (unless SuperAdmin).
- AcceptTripCommandHandler: trip.TenantId null or != driver.TenantId -> FORBIDDEN (driver is current user; same-tenant enforcement).
- GetTripQueryHandler: access control by owner (passenger/driver) or Admin/Support/SuperAdmin; Admin/Support see only their tenant's trips unless SuperAdmin (tenant scoping in query or logic).

---

## Endpoints that allow cross-tenant (SuperAdmin)

- SuperAdmin can operate cross-tenant only when X-Tenant-Id is sent (override). Then AssignDriver and CreatePayment allow trip from another tenant (handler skips tenant match when SuperAdmin). GetAdminTrips and GetAdminDrivers: ApplyTenantScope does not restrict SuperAdmin (they see all when scope not applied to SuperAdmin).
- Endpoints that do not use RequireTenant but still enforce tenant in handler: GET trips/{id} (owner or admin with tenant scope), POST accept (driver.TenantId == trip.TenantId), POST complete (no tenant check; uses trip tenant for tariff), POST payments (trip.TenantId == context unless SuperAdmin).

---

## Public endpoints (no [Authorize] or AllowAnonymous)

- POST api/v1/auth/register: Public.
- POST api/v1/auth/login: Public.
- POST api/v1/auth/refresh: Public.
- POST api/v1/auth/logout: Public.
- POST api/v1/payments/webhook: AllowAnonymous.
- POST api/v1/payments/simulate-webhook: AllowAnonymous (returns 404 if not Development).

All other documented endpoints require [Authorize] with or without specific roles.

---

## Endpoints that use Idempotency-Key

- POST api/v1/trips: Idempotency-Key header required; missing -> 400 IDEMPOTENCY_KEY_REQUIRED. Handler uses idempotency store and returns cached TripDto on replay.
- POST api/v1/payments: Idempotency-Key header required; same. Handler uses idempotency store and returns cached PaymentDto on replay.
- POST api/v1/payments/webhook: Idempotent by event id (Stripe event id), not by Idempotency-Key header.
