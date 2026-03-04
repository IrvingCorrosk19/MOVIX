# QA Release Readiness Report — Movix API v1
**Date:** 2026-03-04
**Environment:** Local dev (docker-compose) — PostgreSQL 18, Redis, PostGIS
**API URL:** `http://127.0.0.1:55392`
**QA Method:** Real HTTP calls only. Zero simulation. Zero hypothetical. All evidence captured live.
**Auditor:** Principal QA Engineer + Staff Backend Engineer

---

## Executive Summary

The Movix API implements a ride-hailing lifecycle (request → assign → arrive → start → complete) with CQRS/MediatR, optimistic concurrency, idempotency, and multi-tenancy. This report covers five validation layers executed against a live instance.

**Three bugs were confirmed by live HTTP evidence:**

| # | Severity | Description |
|---|----------|-------------|
| BUG-004 | **CRITICAL** | `DriverStatusCommandHandler` never resets `CurrentTripId = null` when toggling Online on an existing record. Causes permanent `NO_DRIVERS_AVAILABLE` unless manually corrected in DB. |
| BUG-005 | **HIGH / SECURITY** | Tenant isolation not enforced: passengers can GET any trip they own regardless of `X-Tenant-Id` header; admins bypass tenant check via JWT claim. Trip creation ignores the `X-Tenant-Id` header entirely. |
| BUG-006 | LOW | `NO_DRIVERS_AVAILABLE` error mapped to HTTP 409 (Conflict). Semantically incorrect; should be HTTP 503 or 422. |

**Lifecycle works correctly** once BUG-004 is fixed (required manual DB intervention to reproduce clean state in this test run). **Concurrency is protected** by the state-machine guard. **Idempotency works** for both trips and payments.

---

## Verdict

```
╔══════════════════════════════════════════════════════╗
║                                                      ║
║   NOT READY FOR PRODUCTION                           ║
║                                                      ║
║   BLOCKER: BUG-004 (driver availability stuck)       ║
║   BLOCKER: BUG-005 (tenant isolation breach)         ║
║                                                      ║
║   READY FOR STAGING after BUG-004 + BUG-005 fixed   ║
║                                                      ║
╚══════════════════════════════════════════════════════╝
```

---

## PASS/FAIL Matrix

| Layer | Test | Result | HTTP |
|-------|------|--------|------|
| **1. Infrastructure** | Health — PostgreSQL | PASS | 200 |
| **1. Infrastructure** | Health — Redis | PASS | 200 |
| **1. Infrastructure** | Health — Outbox | PASS | 200 |
| **1. Infrastructure** | Health — PostGIS | PASS | 200 |
| **2. Auth** | Admin login | PASS | 200 |
| **2. Auth** | Driver login | PASS | 200 |
| **2. Auth** | Passenger register + login | PASS | 202/200 |
| **3. Lifecycle** | Create trip (Requested) | PASS | 200 |
| **3. Lifecycle** | Assign driver (→ Accepted) | PASS* | 200 |
| **3. Lifecycle** | Driver arrive (→ DriverArrived) | PASS | 200 |
| **3. Lifecycle** | Driver start (→ InProgress) | PASS | 200 |
| **3. Lifecycle** | Driver complete (→ Completed) | PASS | 200 |
| **3. Lifecycle** | GET trip — final status Completed | PASS | 200 |
| **3. Lifecycle** | Create payment (→ Pending) | PASS | 200 |
| **4. Idempotency** | Duplicate trip (same key → same id) | PASS | 200 |
| **4. Idempotency** | Duplicate payment (same key) | PASS | 200 |
| **5. Concurrency** | 2x assign-driver in parallel (1 wins, 1 rejected) | PASS | 200+400 |
| **6. Multi-tenant** | GET trip with correct tenant | PASS | 200 |
| **6. Multi-tenant** | GET trip — passenger wrong tenant | **FAIL BUG-005** | 200 (exp 403/404) |
| **6. Multi-tenant** | GET trip — admin wrong tenant | **FAIL BUG-005** | 200 (exp 403/404) |
| **6. Multi-tenant** | Create trip with wrong tenant header | **FAIL BUG-005** | 200 (exp 400/403) |

> *PASS with caveat: required manual DB fix (`UPDATE driver_availability SET "CurrentTripId" = NULL`) to unblock BUG-004. Without this fix, assign-driver returns `409 NO_DRIVERS_AVAILABLE`.

---

## Layer 1 — Infrastructure

**Endpoint:** `GET /health`

```json
{
  "status": "Healthy",
  "totalDuration": "...",
  "entries": {
    "postgres":  { "status": "Healthy" },
    "redis":     { "status": "Healthy" },
    "outbox":    { "status": "Healthy" },
    "postgis":   { "status": "Healthy" }
  }
}
```

All four backing services alive. No issues.

---

## Layer 2 — Full Ride Lifecycle

**Trip ID used:** `09238aae-de00-41b0-8286-c919b0cbb93b`
**Payment ID:** `88068e9a-10e6-4da0-ad5d-5d86201e0fdb`

### Correct lifecycle flow

```
POST /trips                      → 200  status=Requested
POST /trips/{id}/assign-driver   → 200  status=Accepted      (admin assigns, not driver accept)
POST /trips/{id}/arrive          → 200  status=DriverArrived
POST /trips/{id}/start           → 200  status=InProgress
POST /trips/{id}/complete        → 200  status=Completed
GET  /trips/{id}                 → 200  status=Completed (confirmed)
POST /payments                   → 200  id=88068e9a... status=Pending
```

> **Note on flow:** The `/accept` endpoint (driver self-assigns with VehicleId) is a SEPARATE flow from admin `assign-driver`. Calling both on the same trip causes `INVALID_TRANSITION` because `assign-driver` already moves the trip to `Accepted`. The tested flow (admin assigns → driver arrive/start/complete) is the production-intended path.

### State machine (TripStateMachine.cs)

```
Requested → Accepted → DriverArrived → InProgress → Completed
     ↓           ↓           ↓              ↓
  Cancelled   Cancelled   Cancelled      Cancelled
```

### HTTP Evidence

**Create Trip:**
```
POST /api/v1/trips
Response 200: {"id":"09238aae-de00-41b0-8286-c919b0cbb93b","status":"Requested",...}
```

**Assign Driver:**
```
POST /api/v1/trips/09238aae-de00-41b0-8286-c919b0cbb93b/assign-driver
Response 200: {"id":"09238aae-...","status":"Accepted","pickupAddress":"..."}
```

**Complete:**
```
POST /api/v1/trips/09238aae-.../complete  body={"distanceKm":5.2,"durationMinutes":18}
Response 200: {"id":"09238aae-...","status":"Completed",...}
```

**Payment:**
```
POST /api/v1/payments  body={"tripId":"09238aae-...","amount":15.00,"currency":"USD"}
Response 200: {"id":"88068e9a-10e6-4da0-ad5d-5d86201e0fdb","status":"Pending"}
```

---

## Layer 3 — Idempotency

| Test | Idempotency-Key | First Response | Second Response | Result |
|------|----------------|----------------|-----------------|--------|
| Trip | `qa-main-<TS>` | HTTP 200, id=09238aae... | HTTP 200, id=09238aae... | **PASS** (same id) |
| Payment | `qa-pay-<TS>` | HTTP 200, status=Pending | HTTP 200, id=88068e9a... | **PASS** |

Idempotency is implemented via Redis key store. Duplicate requests with same key return the cached response without re-executing the command.

---

## Layer 4 — Concurrency

**Scenario:** Two simultaneous `POST /trips/{id}/assign-driver` on the same trip.
**Trip ID:** `d02c24ef-c8c9-49d6-b2c4-229e66a788bd`
**Method:** `Start-Job` (PowerShell parallel background jobs — true concurrent HTTP)

```
[Concurrent R1] HTTP 200
Body: {"id":"d02c24ef-...","status":"Accepted","pickupAddress":"Conc A",...}

[Concurrent R2] HTTP 400
Body: {"error":"Trip is not in Requested state","code":"TRIP_INVALID_STATE"}

RESULT: PASS — Exactly one succeeded (R1=200, R2=400)
```

### How concurrency protection works

The `AssignDriverCommandHandler` sets `DriverAvailability.CurrentTripId` inside a unit of work with `IsConcurrencyToken()` on the `RowVersion` column (bytea). Additionally, the `TripStateMachine` guards the transition: once R1 moves the trip to `Accepted`, R2's attempt to transition from `Requested → Accepted` is rejected with `TRIP_INVALID_STATE`.

**Important:** The `IsConcurrencyToken()` is declared WITHOUT `IsRowVersion()`. The `RowVersion` column has a `HasDefaultValueSql("gen_random_bytes(8)")` on INSERT but **no database trigger to update it on UPDATE**. This means optimistic concurrency via EF Core's `DbUpdateConcurrencyException` may not fire on concurrent updates if both readers see the same byte value. The state-machine guard is the actual protection here.

**DB state after concurrency test:**
```
DriverId:       adf2ab51-0bc7-4a64-9787-9fd455ae9897
IsOnline:       true
CurrentTripId:  d02c24ef-c8c9-49d6-b2c4-229e66a788bd  (Accepted)
```

---

## Layer 5 — Multi-Tenant Isolation

**Correct tenant:** `00000000-0000-0000-0000-000000000001`
**Wrong tenant:** `00000000-0000-0000-0000-000000000099`
**Trip ID:** `2e8538e4-cf30-4158-8001-98cb63a5fba7`

| Test | Expected | Actual | Result |
|------|----------|--------|--------|
| GET with correct tenant | 200 | 200 | PASS |
| Passenger GET with wrong X-Tenant-Id | 403 or 404 | **200** | **FAIL BUG-005** |
| Admin GET with wrong X-Tenant-Id | 403 or 404 | **200** | **FAIL BUG-005** |
| Create trip with wrong X-Tenant-Id | 400 or 403 | **200 (created)** | **FAIL BUG-005** |

**Root cause (GetTripQueryHandler.cs):**

```csharp
var isOwner = userId == trip.PassengerId
           || (currentUserDriverId.HasValue && currentUserDriverId == trip.DriverId);

// Tenant check only runs for admins/support who are NOT owners:
if (isAdminOrSupport && !isOwner && !_tenantContext.IsSuperAdmin) {
    if (trip.TenantId.HasValue && trip.TenantId != _tenantContext.TenantId)
        return Result<TripDetailDto>.Failure("Trip not found", "TRIP_NOT_FOUND");
}
// If isOwner == true, tenant check is SKIPPED entirely
```

**Root cause (TenantContext):** `TenantContext` reads `TenantId` from the **JWT claim** (`tenant_id`), not from the `X-Tenant-Id` header. When a user presents a token issued for tenant `001` but sends `X-Tenant-Id: 099`, the `TenantContext.TenantId` still equals `001` (from JWT). The header is read but never validated against the token claim.

**Root cause (Create trip):** `TripsController.CreateTrip` uses `_tenantContext.TenantId` (from JWT) to set `trip.TenantId`, ignoring the `X-Tenant-Id` header. There is no mismatch check between the header and the token's tenant claim.

---

## Bug Registry

### BUG-004 — CRITICAL — DriverStatusCommandHandler: CurrentTripId not reset

**File:** `src/Movix.Application/Drivers/Commands/DriverStatus/DriverStatusCommandHandler.cs`

**Behavior:** When a `DriverAvailability` record already exists and the driver sets status to Online, the handler updates `IsOnline = true` but **never sets `CurrentTripId = null`**. If `CurrentTripId` holds a reference to a prior completed trip (from a previous session or after a RowVersion mismatch prevented the reset in `TransitionTripCommandHandler`), the driver is permanently invisible to `GetFirstAvailableAsync` (which filters `CurrentTripId IS NULL`).

**Impact in this test run:** The seeded driver had `CurrentTripId = 00f477e9-bc04-4cd3-9bc8-dde5450f2c36` (status=Completed). Every call to `assign-driver` returned `409 NO_DRIVERS_AVAILABLE`. Required manual `UPDATE driver_availability SET "CurrentTripId" = NULL` in PostgreSQL to unblock.

**Production risk:** Any trip that ends without properly clearing the driver (e.g., server crash mid-transaction, RowVersion conflict on complete, or any future similar edge case) permanently locks the driver. There is **no self-healing mechanism**. Requires operator DB intervention.

**Fix:** In the `else` branch of `DriverStatusCommandHandler` (existing record), add:
```csharp
// When driver goes Online, always clear any stale trip assignment
if (request.Status == DriverStatus.Online)
    availability.CurrentTripId = null;
```

---

### BUG-005 — HIGH / SECURITY — Tenant Isolation Bypass

**Files:**
- `src/Movix.Application/Trips/Queries/GetTrip/GetTripQueryHandler.cs`
- `src/Movix.Application/Trips/Commands/CreateTrip/CreateTripCommandHandler.cs`

**Behavior:** Three isolation failures confirmed:
1. A passenger can GET any trip they own using a mismatched `X-Tenant-Id` header (isOwner check bypasses all tenant validation)
2. An admin can GET trips cross-tenant because `TenantContext.TenantId` comes from the JWT, not the header
3. Trips can be created with a mismatched `X-Tenant-Id` header (header is ignored; JWT tenant is used)

**Impact:** In a multi-tenant SaaS deployment, a passenger who belongs to tenant A could technically call `GET /trips/{id}` with `X-Tenant-Id: B` and still receive the trip data if they own it. More importantly, the design allows the header to diverge from the JWT claim without detection, creating an inconsistent authorization model.

**Note:** In the current single-tenant dev setup this has no immediate user impact. In a production multi-tenant deployment it is a SECURITY BLOCKER.

**Fix options:**
1. Enforce header/JWT tenant consistency in middleware: reject requests where `X-Tenant-Id` header ≠ JWT `tenant_id` claim
2. Remove the `isOwner` bypass from the tenant guard — isOwner should not skip tenant validation
3. Always validate `trip.TenantId == _tenantContext.TenantId` regardless of role or ownership

---

### BUG-006 — LOW — NO_DRIVERS_AVAILABLE mapped to HTTP 409

**File:** `src/Movix.Api/Controllers/V1/TripsController.cs`

**Behavior:** `MapError` maps `"NO_DRIVERS_AVAILABLE"` to `Conflict()` (HTTP 409). HTTP 409 Conflict means "the request conflicts with the current state of the resource." No conflict exists when there are simply no available drivers — it is a service capacity issue.

**Correct mapping:** HTTP 503 Service Unavailable (transient, retry later) or HTTP 422 Unprocessable Entity (business rule rejection).

**Impact:** Low. Client integrators may interpret 409 as a write conflict requiring resolution, not a transient retry scenario.

---

## Residual Risks

| Risk | Severity | Description |
|------|----------|-------------|
| RowVersion not updated on UPDATE | MEDIUM | `DriverAvailability.RowVersion` uses `gen_random_bytes(8)` as INSERT default but no trigger updates it on UPDATE. True optimistic concurrency via `DbUpdateConcurrencyException` will only fire if both writers read the same byte value — unlikely but possible. The state-machine guard compensates. |
| Outbox not monitored | LOW | Outbox pattern is healthy but there is no dead-letter monitoring or retry exhaustion alerting in the current setup. |
| Payment stays Pending | LOW | After `POST /payments` the payment status is `Pending`. No payment gateway integration in scope — no Completed state tested. |
| No rate limiting observed | LOW | No 429 responses detected on repeated calls. No rate limiting headers. |
| Driver ABAC (assign-driver) | LOW | `TransitionTripCommandHandler` verifies `trip.DriverId == currentDriverId` for driver-only transitions (arrive, start, complete). Confirmed working in tests. |

---

## Test Artifacts

| File | Description |
|------|-------------|
| `tests/qa_correct_lifecycle.ps1` | Full lifecycle test (corrected flow) |
| `tests/qa_concurrency_only.ps1` | Isolated concurrency test |
| `tests/qa_multitenant.ps1` | Tenant isolation tests |
| `tests/qa_concurrency_result.json` | Live concurrency test output |
| `tests/qa_multitenant_result.json` | Live multi-tenant test output |

---

## Sign-off Checklist

| Condition | Status |
|-----------|--------|
| Full lifecycle works without manual DB intervention | **NO** (BUG-004 required manual reset) |
| Concurrent assign-driver does not double-assign | YES |
| No `DbUpdateConcurrencyException` 500s in normal flow | YES |
| No inconsistent states in DB after complete | YES |
| Tenant isolation enforced for all users | **NO** (BUG-005) |
| Idempotency keys prevent duplicate resources | YES |
| Infrastructure healthy | YES |

**2 of 7 conditions failed. PRODUCTION RELEASE BLOCKED.**

Fix BUG-004 + BUG-005, re-run this suite, then re-evaluate for staging promotion.
