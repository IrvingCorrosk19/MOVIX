# Stress Test — Assign-Driver Endpoint

**Endpoint:** `POST /api/v1/trips/{id}/assign-driver`  
**Purpose:** Validate concurrency stability and production-safety of the assign-driver flow.  
**Reference:** Concurrency behavior is documented in [docs/ASSIGN_DRIVER_CONCURRENCY_FLOW.md](ASSIGN_DRIVER_CONCURRENCY_FLOW.md).

---

## What the toolkit validates

1. **No double assignment** — A driver is never assigned to two trips simultaneously.
2. **Correct error mapping** — Concurrency conflicts are returned as **409 CONCURRENCY_CONFLICT** (not 500 or NO_DRIVERS_AVAILABLE).
3. **No HTTP 500** — Under stress, the API must not return 500.
4. **Consistent database state** — No driver appears as assigned to more than one Accepted trip.

---

## Artifacts

| Artifact | Purpose |
|----------|---------|
| **scripts/stress/assign-driver-stress.ps1** | Runs 50 concurrent assign-driver requests (one driver, 50 trips); prints summary table and SYSTEM_STABLE. |
| **scripts/stress/assign-driver-load.ps1** | Optional load test: 200 concurrent requests; detects race conditions, multiple assignments, unexpected 500. |
| **scripts/stress/check-driver-consistency.sql** | SQL to detect drivers with more than one Accepted trip (run after stress test to verify DB consistency). |
| **scripts/stress/capture-concurrency-logs.ps1** | Starts the API and captures lines containing "Concurrency conflict" into **logs/concurrency-diagnostics.log**. |
| **scripts/stress/log-capture.ps1** | Alternative: same as capture-concurrency-logs.ps1 (starts API, filters to logs/concurrency-diagnostics.log). |
| **docs/STRESS_TEST_ASSIGN_DRIVER.md** | This document. |

---

## How to run the stress test

### Prerequisites

- API dependencies running (PostgreSQL, Redis, etc.).
- API not yet running in another process (or use the same API for the stress test and run stress from another machine/terminal).

### Step 1: Start the API

In one terminal:

```powershell
cd c:\Proyectos\RiderFlow
dotnet run --project src/Movix.Api
```

Leave it running.

### Step 2: (Optional) Start log capture

If you want to capture "Concurrency conflict" lines to a file, in a **second** terminal run:

```powershell
cd c:\Proyectos\RiderFlow
.\scripts\stress\capture-concurrency-logs.ps1
```

This starts the API and pipes output to the log filter. **Note:** You cannot run both the normal API and capture-concurrency-logs at the same time on the same port. So either:

- Run the API normally (Step 1) and watch the console for "Concurrency conflict" lines, or  
- Run **only** `capture-concurrency-logs.ps1` (it starts the API and writes matching lines to `logs/concurrency-diagnostics.log`).

For a single stress run, running the API normally (Step 1) and observing the console is usually enough.

### Step 3: Run the stress script

In another terminal (with the API already running from Step 1):

```powershell
cd c:\Proyectos\RiderFlow
.\scripts\stress\assign-driver-stress.ps1
```

The script will:

1. Log in as admin and driver.
2. Set the driver online.
3. Create 50 trips.
4. Launch 50 concurrent assign-driver requests (one per trip).
5. Print a summary of HTTP 200, 409, 400, 500 counts and **DuplicateDriverAssignmentsDetected**.

### Step 4: Check database consistency (optional)

After the stress run, run the SQL check against your Movix database:

```powershell
psql -h localhost -U movix -d movix_core -f scripts/stress/check-driver-consistency.sql
```

Or run the query inside your SQL client. If it returns **no rows**, no driver has more than one Accepted trip (consistent). If it returns rows, a driver has multiple Accepted trips (BUG).

---

## Expected results

When running 50 concurrent assign-driver requests with **one** driver online and **50** trips:

| Result | Meaning |
|--------|--------|
| **200** | Successful assignment. Exactly **one** request should succeed. |
| **409** | Expected concurrency conflict (CONCURRENCY_CONFLICT). The remaining requests should return this. |
| **400** | Trip invalid state (e.g. TRIP_INVALID_STATE). May appear if a request targets an already-assigned trip. |
| **500** | **BUG** — Server error. Must not occur under correct behavior. |

### Expected summary (pass)

Example output from the stress script:

```text
TotalRequests       = 50
Success200          = 1
Conflict409         = 49
InvalidState400     = 0
ServerErrors500     = 0

DuplicateDriverAssignmentsDetected = false
SYSTEM_STABLE = true
```

Equivalently:

- **TotalRequests:** 50  
- **200 OK:** 1  
- **409 CONCURRENCY_CONFLICT:** 49  
- **400 TRIP_INVALID_STATE:** 0  
- **500 errors:** 0  
- **DuplicateDriverAssignmentsDetected:** false  
- **SYSTEM_STABLE:** true  

**SYSTEM_STABLE = true** when all of the following hold:

- No HTTP 500 (ServerErrors500 = 0).
- No duplicate driver assignments (DuplicateDriverAssignmentsDetected = false).
- Only one successful assign per driver (Success200 ≤ 1).

### Failure indicators

- **DuplicateDriverAssignmentsDetected = true** — More than one request returned 200; same driver was assigned to multiple trips (BUG).
- **ServerErrors500 > 0** — Server errors under load (BUG).
- **SYSTEM_STABLE = false** — One or more of: 500s, duplicate assignments, or more than one success per driver.
- **check-driver-consistency.sql returns rows** — At least one driver has multiple Accepted trips in the DB (BUG).

---

## How to interpret results

1. **One 200, rest 409, zero 500** — Concurrency is handled as designed; conflicts are translated to 409 CONCURRENCY_CONFLICT.
2. **Multiple 200s** — Double (or more) assignment occurred; investigate assign-driver and DriverAvailability concurrency (e.g. RowVersion, atomic reservation).
3. **Any 500** — Check API logs and UnitOfWork forensic logging; fix exception handling or concurrency logic.
4. **SQL returns rows** — Database is inconsistent; run the SQL after each stress run to confirm no residual duplicate assignments.

---

## Log capture (concurrency diagnostics)

**logs/concurrency-diagnostics.log** contains only lines that include the text **"Concurrency conflict"** (the UnitOfWork forensic log line). Each line is prefixed with a timestamp when it was written.

Example line in the log:

```text
[2026-03-04 12:00:00] Concurrency conflict. Entity=DriverAvailability PrimaryKey=DriverId=xxx State=Modified ConcurrencyToken=RowVersion: Original=0x... Current=0x...
```

Use this file to confirm that conflicts are on the expected entity (e.g. DriverAvailability) and to inspect token values when debugging.

---

## Optional: Load test (200 requests)

Run a heavier load to detect race conditions and unexpected 500s:

```powershell
.\scripts\stress\assign-driver-load.ps1
```

Expect: 1× 200, rest 409, 0× 500, DuplicateDriverAssignmentsDetected = false, SYSTEM_STABLE = true. Any 500 or multiple 200s indicates a concurrency or stability issue.

---

## Summary

| Action | Command / File |
|--------|----------------|
| Run stress test (50 req) | `.\scripts\stress\assign-driver-stress.ps1` |
| Run load test (200 req) | `.\scripts\stress\assign-driver-load.ps1` |
| Check DB consistency | `psql ... -f scripts/stress/check-driver-consistency.sql` |
| Capture concurrency log lines | `.\scripts\stress\capture-concurrency-logs.ps1` (starts API, writes to logs/concurrency-diagnostics.log) |
| Expected (pass) | TotalRequests: 50, Success200: 1, Conflict409: 49, ServerErrors500: 0, DuplicateDriverAssignmentsDetected: false, SYSTEM_STABLE: true |
