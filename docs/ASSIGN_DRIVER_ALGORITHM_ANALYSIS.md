# Assign-Driver Algorithm Analysis

**Endpoint:** `POST /api/v1/trips/{id}/assign-driver`  
**Project:** Movix API (ASP.NET Core, PostgreSQL, PostGIS, Clean Architecture)  
**Analysis type:** Read-only — no code changes.

---

## 1. Architecture flow diagram

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│  HTTP POST /api/v1/trips/{id}/assign-driver                                      │
│  [Authorize(Roles = "Admin,Support")] [RequireTenant] [RateLimit: trips]        │
└─────────────────────────────────────────────────────────────────────────────────┘
                                        │
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│  TripsController.AssignDriver(Guid id)                                           │
│  → _mediator.Send(new AssignDriverCommand(id))                                   │
└─────────────────────────────────────────────────────────────────────────────────┘
                                        │
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│  AssignDriverCommandHandler.Handle(AssignDriverCommand)                          │
│  1. Authorization: Admin | Support | SuperAdmin only                             │
│  2. Load trip: _tripRepository.GetByIdAsync(tripId)                               │
│  3. Trip state: must be Requested                                                 │
│  4. Tenant: trip.TenantId must match _tenantContext.TenantId (unless SuperAdmin)  │
│  5. Select driver: _availabilityRepository.GetFirstAvailableAsync()               │
│  6. Tenant check: availability.Driver.TenantId == trip.TenantId (unless SuperAdmin)│
│  7. Mutate: trip.DriverId, trip.Status=Accepted, trip.UpdatedAtUtc/By             │
│  8. Add: TripStatusHistory (Requested → Accepted)                                │
│  9. Mutate: availability.CurrentTripId = trip.Id, availability.UpdatedAtUtc        │
│ 10. Add: OutboxMessage (DriverAssigned)                                           │
│ 11. _uow.SaveChangesAsync() → single transaction                                  │
│ 12. On DbUpdateConcurrencyException → ConcurrencyException → return CONFLICT     │
│ 13. Audit log; return success                                                     │
└─────────────────────────────────────────────────────────────────────────────────┘
                                        │
          ┌─────────────────────────────┼─────────────────────────────┐
          ▼                             ▼                             ▼
┌──────────────────┐    ┌──────────────────────────────┐    ┌──────────────────┐
│ ITripRepository  │    │ IDriverAvailabilityRepository │    │ IUnitOfWork      │
│ GetByIdAsync(id) │    │ GetFirstAvailableAsync()      │    │ SaveChangesAsync │
└────────┬─────────┘    └───────────────┬──────────────┘    └────────┬─────────┘
         │                              │                              │
         ▼                              ▼                              ▼
┌──────────────────┐    ┌──────────────────────────────┐    ┌──────────────────┐
│ TripRepository   │    │ DriverAvailabilityRepository │    │ UnitOfWork       │
│ EF: Trips        │    │ EF: DriverAvailabilities     │    │ DbContext        │
│ + Include(       │    │ + Include(Driver)             │    │ SaveChangesAsync │
│   StatusHistory) │    │ WHERE IsOnline AND           │    │ → catches        │
│ WHERE Id = id    │    │       CurrentTripId IS NULL   │    │   DbUpdateConcur-│
│                  │    │ [tenant filter on Driver]     │    │   rencyException │
│                  │    │ ORDER BY UpdatedAtUtc        │    │   → ConcurrencyEx-│
│                  │    │ FirstOrDefaultAsync          │    │   ception         │
└────────┬─────────┘    └───────────────┬──────────────┘    └──────────────────┘
         │                              │
         ▼                              ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│  PostgreSQL                                                                       │
│  Tables: trips, trip_status_histories, driver_availability, drivers, outbox_messages│
│  Concurrency: RowVersion (bytea) on Trip and DriverAvailability (IsConcurrencyToken)│
└─────────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Step-by-step execution

| Step | Layer | Action |
|------|--------|--------|
| 1 | **Controller** | `TripsController.AssignDriver(id)` receives GUID from route. Sends `AssignDriverCommand(id)` via MediatR. No request body. |
| 2 | **Validation** | `AssignDriverCommandValidator`: `TripId` must be non-empty (FluentValidation). |
| 3 | **Handler – Authorization** | Only `Admin`, `Support`, or `SuperAdmin` may proceed; otherwise returns `FORBIDDEN`. |
| 4 | **Handler – Load trip** | `_tripRepository.GetByIdAsync(request.TripId)`. EF query: `Trips` + `Include(StatusHistory)`, `FirstOrDefaultAsync(t => t.Id == id)`. If null → `TRIP_NOT_FOUND`. |
| 5 | **Handler – Trip state** | `trip.Status` must equal `TripStatus.Requested`. Otherwise → `TRIP_INVALID_STATE`. |
| 6 | **Handler – Tenant (trip)** | If not SuperAdmin and `trip.TenantId` has value: must equal `_tenantContext.TenantId`. Otherwise → `FORBIDDEN`. |
| 7 | **Handler – Driver selection** | `_availabilityRepository.GetFirstAvailableAsync()`. Single query returning one `DriverAvailability` with `Driver` included (see section 3). If null → `NO_DRIVERS_AVAILABLE`. |
| 8 | **Handler – Tenant (driver)** | If not SuperAdmin and `availability.Driver` and `trip.TenantId` set: `availability.Driver.TenantId` must equal `trip.TenantId`. Otherwise → `FORBIDDEN`. |
| 9 | **Handler – Mutate trip** | `trip.DriverId = availability.DriverId`, `trip.Status = Accepted`, `trip.UpdatedAtUtc`, `trip.UpdatedBy`. |
| 10 | **Handler – History** | `_tripRepository.AddStatusHistoryAsync(history)` — new row `FromStatus=Requested`, `ToStatus=Accepted`. |
| 11 | **Handler – Mutate availability** | `availability.CurrentTripId = trip.Id`, `availability.UpdatedAtUtc = now`. |
| 12 | **Handler – Outbox** | `_outboxRepository.AddAsync(OutboxMessage Type=DriverAssigned)`. |
| 13 | **Handler – Persist** | `_uow.SaveChangesAsync()`. Single transaction: UPDATE trip, INSERT status history, UPDATE driver_availability, INSERT outbox (and audit later). |
| 14 | **Handler – Concurrency** | If `DbUpdateConcurrencyException` (e.g. RowVersion mismatch): caught in `UnitOfWork`, rethrown as `ConcurrencyException`; handler returns `CONFLICT`. |
| 15 | **Handler – Audit** | `_audit.LogAsync("AssignDriver", "Trip", trip.Id, { driverId })`. |
| 16 | **Controller** | Maps success to 200 OK with `TripDto`; errors to 403/404/409/422 via `MapError`. |

---

## 3. Repository queries and SQL/EF

### 3.1 Trip load — `ITripRepository.GetByIdAsync(Guid id)`

**Implementation:** `TripRepository.GetByIdAsync`

**EF usage:**
```csharp
_db.Trips
    .Include(t => t.StatusHistory)
    .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
```

**SQL (conceptually):**
- One query (or two if EF splits includes): load `trips` row by primary key `Id`; load related `trip_status_histories` for that trip.
- Index: primary key on `trips.Id` → single-row lookup.

---

### 3.2 Driver selection — `IDriverAvailabilityRepository.GetFirstAvailableAsync()`

**Implementation:** `DriverAvailabilityRepository.GetFirstAvailableAsync`

**EF usage:**
```csharp
var query = _db.DriverAvailabilities
    .Include(x => x.Driver)
    .Where(x => x.IsOnline && x.CurrentTripId == null);

if (!_tenantContext.IsSuperAdmin && _tenantContext.TenantId.HasValue)
    query = query.Where(x => x.Driver.TenantId == _tenantContext.TenantId.Value);

return await query
    .OrderBy(x => x.UpdatedAtUtc)
    .FirstOrDefaultAsync(cancellationToken);
```

**Equivalent SQL (conceptually):**
```sql
SELECT da."DriverId", da."IsOnline", da."CurrentTripId", da."UpdatedAtUtc", da."RowVersion",
       d."Id", d."UserId", d."TenantId", d."Status", ...
FROM driver_availability da
INNER JOIN drivers d ON d."Id" = da."DriverId"
WHERE da."IsOnline" = true
  AND da."CurrentTripId" IS NULL
  AND (d."TenantId" = @tenantId OR @isSuperAdmin = true)
ORDER BY da."UpdatedAtUtc" ASC
LIMIT 1;
```

**Indexes (from `DriverAvailabilityConfiguration`):**
- Composite: `(IsOnline, CurrentTripId)` on `driver_availability`.
- Tenant filter is on `drivers.TenantId`; `drivers` has index on `TenantId`.

**Ordering:** `OrderBy(UpdatedAtUtc)` — “oldest updated first”. No index on `driver_availability.UpdatedAtUtc` in the configuration reviewed; ordering may be in-memory after index scan or use of a sort in the database.

---

### 3.3 SaveChanges — what gets written

In one transaction:

| Entity | Operation |
|--------|-----------|
| `Trip` | UPDATE (DriverId, Status, UpdatedAtUtc, UpdatedBy, RowVersion checked) |
| `TripStatusHistory` | INSERT (new row) |
| `DriverAvailability` | UPDATE (CurrentTripId, UpdatedAtUtc, RowVersion checked) |
| `OutboxMessage` | INSERT |
| (Later, audit) | INSERT audit log (if same UoW or separate) |

EF generates:
- `UPDATE trips SET "DriverId" = ..., "Status" = ..., "UpdatedAtUtc" = ..., "UpdatedBy" = ..., "RowVersion" = ... WHERE "Id" = ... AND "RowVersion" = ...`
- `UPDATE driver_availability SET "CurrentTripId" = ..., "UpdatedAtUtc" = ..., "RowVersion" = ... WHERE "DriverId" = ... AND "RowVersion" = ...`

If another request updated the same row and changed `RowVersion`, one of these UPDATEs can affect 0 rows and trigger `DbUpdateConcurrencyException`.

---

## 4. Driver availability checks

| Check | Where | Logic |
|-------|--------|------|
| Online | Repository query | `Where(x => x.IsOnline && x.CurrentTripId == null)` — only online drivers with no current trip. |
| Free (no current trip) | Repository query | `CurrentTripId == null`. |
| Tenant | Repository + handler | Repository: `Driver.TenantId == _tenantContext.TenantId` (unless SuperAdmin). Handler: defense-in-depth `availability.Driver.TenantId != trip.TenantId` → FORBIDDEN. |

No check on:
- Driver location / distance to pickup
- Driver rating or capacity
- Time since last trip
- Explicit “availability window”

---

## 5. Trip state validation

| Check | Location | Rule |
|-------|----------|------|
| Exists | Handler | `GetByIdAsync` returns null → `TRIP_NOT_FOUND`. |
| State | Handler | `trip.Status == TripStatus.Requested`; otherwise `TRIP_INVALID_STATE`. |

No use of a formal state machine in this handler; it’s a single-state check. Transition is: `Requested → Accepted`.

---

## 6. Concurrency control

| Mechanism | Where | Behavior |
|-----------|--------|----------|
| **Optimistic (Trip)** | `Trip` entity | `RowVersion` (bytea), `IsConcurrencyToken()`, default `gen_random_bytes(8)` on INSERT. UPDATE includes `WHERE RowVersion = @old`. |
| **Optimistic (DriverAvailability)** | `DriverAvailability` entity | Same: `RowVersion` (bytea), `IsConcurrencyToken()`. UPDATE by `DriverId` + `RowVersion`. |
| **UnitOfWork** | Single `SaveChangesAsync` | Trip update, history insert, availability update, outbox insert in one transaction. |
| **Exception handling** | `UnitOfWork.SaveChangesAsync` | Catches `DbUpdateConcurrencyException`, logs entries, throws `ConcurrencyException`. |
| **Handler** | AssignDriverCommandHandler | Catches `ConcurrencyException`, returns `CONFLICT` (e.g. 409). |

**Note:** `RowVersion` is set only on INSERT (default). There is no database trigger or application logic in the reviewed code that updates `RowVersion` on UPDATE; PostgreSQL does not auto-update it. So EF sends the same bytea it read; the first UPDATE wins, the second can still see the same “old” row and may get 0 rows affected, triggering concurrency exception. Actual protection in practice is reinforced by the **trip state**: once the first request sets `Status = Accepted`, a second request fails at step 5 (`TRIP_INVALID_STATE`) before any write. So the main guard is state, not only RowVersion.

---

## 7. Geospatial logic

**In the assign-driver flow:** none.

- Trip has `PickupLocation` and `DropoffLocation` (PostGIS geometry) and GIST indexes on them (`TripConfiguration`).
- Assign-driver does not use trip location or driver location. It does not call PostGIS (e.g. distance, containment, or “nearest driver”).
- Driver selection is purely: `IsOnline`, `CurrentTripId == null`, tenant, then `OrderBy(UpdatedAtUtc)`.

So for this endpoint there is **no geospatial filtering or ranking**.

---

## 8. Ordering / ranking logic

| Aspect | Implementation |
|--------|----------------|
| **Order** | Single `OrderBy(x => x.UpdatedAtUtc)` in `GetFirstAvailableAsync`. |
| **Meaning** | “Earliest updated” availability wins — effectively FIFO by last availability update (e.g. when they went online or were last freed). |
| **Ranking** | None: no score, no distance, no rating, no capacity. First row of the ordered query is taken. |

---

## 9. Performance and scalability risks

### 9.1 Full table / large scans

- **Trip load:** By primary key → single row; low risk.
- **Driver availability:** Filter is `IsOnline = true` and `CurrentTripId IS NULL` (and tenant via join). The composite index `(IsOnline, CurrentTripId)` can be used for the predicate. If the table is large and many rows are “available”, PostgreSQL still must:
  - Return all matching rows (or a subset) to sort by `UpdatedAtUtc`.
  - There is **no index on `UpdatedAtUtc`** in the configuration; so sort may be in-memory or filesort.
- **Risk:** For many available drivers, the “find first by UpdatedAtUtc” can touch many rows and sort them → **potential full index scan + sort** on the available subset.

### 9.2 No geospatial filtering

- Pickup/driver location is not used. All available drivers in the tenant are considered equally (except for `UpdatedAtUtc` order).
- **Impact:** Cannot “assign nearest driver” or reduce radius without future changes.

### 9.3 Inefficient ordering

- Ordering by `UpdatedAtUtc` with no supporting index can be expensive when the available set is large.
- **Suggestion (for later):** Consider an index such as `(IsOnline, CurrentTripId, UpdatedAtUtc)` or similar to allow index-only or index-assisted ordering and limit work to a small set.

### 9.4 Locking risks

- No explicit `SELECT ... FOR UPDATE` or pessimistic locking in the reviewed code. Two concurrent assign-driver calls for **different** trips can each get a different driver from `GetFirstAvailableAsync` and then both UPDATE different rows → normal.
- Two concurrent assign-driver calls for the **same** trip: first wins (trip → Accepted), second fails at trip state check (`TRIP_INVALID_STATE`) before persisting. So no double-assign of the same trip.
- Two concurrent assign-driver calls for **different** trips might select the **same** driver if both run `GetFirstAvailableAsync` before either commits. Then:
  - Both UPDATE the same `DriverAvailability` row (same `DriverId`). One UPDATE succeeds, the other can hit 0 rows (RowVersion) → `DbUpdateConcurrencyException` → CONFLICT. So one request fails at SaveChanges.
- **Risk:** Under high concurrency, many requests can repeatedly pick the same “first” driver and retry; no skips or backoff in the algorithm.

### 9.5 N+1 queries

- **Trip:** One query with `Include(StatusHistory)` — no N+1 for status history.
- **Driver selection:** One query with `Include(Driver)` — single JOIN, no N+1.
- **Audit:** Depends on `IAuditService` implementation; not part of the core assign flow. No N+1 identified inside the handler/repository chain for assign-driver.

---

## 10. Scalability limitations (summary)

| Limitation | Severity | Description |
|------------|----------|-------------|
| No location-based selection | High | Cannot prefer or restrict by distance to pickup; all available drivers in tenant treated equally except by `UpdatedAtUtc`. |
| Ordering without index | Medium | `ORDER BY UpdatedAtUtc` with no index on that column can cause sort over all available rows. |
| Single-driver selection | Medium | One driver per request; no batching or “best of N” by distance/rating. |
| Contention on same driver | Medium | Under load, multiple trips can compete for the same “first” driver; one wins, others get CONFLICT and must retry (and may hit same driver again). |
| No explicit backoff/retry | Low | No built-in skip or ranking to avoid repeatedly selecting the same driver on conflict. |
| RowVersion not auto-updated on UPDATE | Low | PostgreSQL does not refresh the bytea on UPDATE; EF’s optimistic check still works when two writers update the same row (one gets 0 rows affected). |

---

## 11. Summary

- **Flow:** Controller → `AssignDriverCommand` → `AssignDriverCommandHandler` → Trip load (GetByIdAsync) → state/tenant checks → `GetFirstAvailableAsync()` (one driver) → tenant check on driver → mutate trip, add history, mutate availability, add outbox → single `SaveChangesAsync` → concurrency handled via `ConcurrencyException` → 200 or 4xx.
- **Driver selection:** One EF query: available = `IsOnline && CurrentTripId == null`, tenant filter via `Driver`, then `OrderBy(UpdatedAtUtc)`, `FirstOrDefaultAsync`. No raw SQL in this path; no geospatial; no ranking beyond “oldest UpdatedAtUtc”.
- **Concurrency:** Optimistic (RowVersion on Trip and DriverAvailability) plus trip state guard (Requested → Accepted). Same-trip concurrent calls: second fails on state. Different trips, same driver: one can get CONFLICT on SaveChanges.
- **Risks:** Possible large sort on available drivers (no index on UpdatedAtUtc), no distance/location logic, and contention when many assignments target the same “first” driver.

---

*Document generated from static analysis of the codebase. No code was modified.*
