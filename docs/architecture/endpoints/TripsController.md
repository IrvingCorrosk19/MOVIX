# TripsController

Base route: `api/v1/trips`  
Controller: `[Authorize]` (all endpoints require authentication).  
Rate limiting: `trips` policy on Create, AssignDriver, Accept.

---

## Endpoint: POST api/v1/trips

### Roles permitidos:
Any authenticated user.

### RequireTenant:
Yes

### Request:
- Header: `Idempotency-Key` (required; missing returns 400 IDEMPOTENCY_KEY_REQUIRED).
- Body DTO: CreateTripRequest
  - PickupLatitude: double (required)
  - PickupLongitude: double (required)
  - DropoffLatitude: double (required)
  - DropoffLongitude: double (required)
  - PickupAddress: string (nullable)
  - DropoffAddress: string (nullable)
  - EstimatedAmount: decimal (nullable)
  - Currency: string (nullable, default USD)

### Response (200):
- TripDto: Id (Guid), Status (string), PickupAddress (string?), DropoffAddress (string?), EstimatedAmount (decimal?), FinalAmount (decimal?), Currency (string), CreatedAtUtc (DateTime)

### Posibles errores:
- IDEMPOTENCY_KEY_REQUIRED: 400
- UNAUTHORIZED: 400
- TENANT_REQUIRED: 400
- (other handler errors): 400

---

## Endpoint: GET api/v1/trips/{id}

### Roles permitidos:
Any authenticated user. Handler enforces: owner (passenger or driver) or Admin/Support/SuperAdmin. Admin/Support see only trips of their tenant unless SuperAdmin.

### RequireTenant:
No (tenant checked inside GetTripQueryHandler by role and trip.TenantId).

### Request:
- id: Guid (route)

### Response (200):
- TripDetailDto: Id, Status, PassengerId, DriverId (nullable), PickupAddress, DropoffAddress, EstimatedAmount, FinalAmount, Currency, CreatedAtUtc, UpdatedAtUtc

### Posibles errores:
- TRIP_NOT_FOUND: 404
- FORBIDDEN: 403

---

## Endpoint: POST api/v1/trips/{id}/assign-driver

### Roles permitidos:
Admin, Support

### RequireTenant:
Yes

### Request:
- id: Guid (route, trip id). No body.

### Response (200):
- TripDto (same shape as Create)

### Posibles errores:
- FORBIDDEN: 403
- TRIP_NOT_FOUND: 404
- TRIP_INVALID_STATE: 400
- NO_DRIVERS_AVAILABLE: 409
- (other): 400

---

## Endpoint: POST api/v1/trips/{id}/accept

### Roles permitidos:
Driver, Admin

### RequireTenant:
No

### Request:
- id: Guid (route)
- Body: AcceptTripRequest — VehicleId: Guid (required)

### Response (200):
- TripDto

### Posibles errores:
- UNAUTHORIZED: 400 (MapError default)
- TRIP_NOT_FOUND: 404
- INVALID_TRANSITION: 422
- DRIVER_NOT_FOUND: 400
- FORBIDDEN: 403
- VEHICLE_NOT_FOUND: 400
- CONFLICT: 409

---

## Endpoint: POST api/v1/trips/{id}/arrive

### Roles permitidos:
Driver, Admin

### RequireTenant:
Yes

### Request:
- id: Guid (route). No body.

### Response (200):
- TripDto

### Posibles errores:
Same as MapError: FORBIDDEN 403, TRIP_NOT_FOUND 404, TRIP_INVALID_STATE 400, NO_DRIVERS_AVAILABLE 409, INVALID_TRANSITION 422, DRIVER_NOT_ASSIGNED 422, CONFLICT 409, default 400.

---

## Endpoint: POST api/v1/trips/{id}/start

### Roles permitidos:
Driver, Admin

### RequireTenant:
Yes

### Request:
- id: Guid (route). No body.

### Response (200):
- TripDto

### Posibles errores:
Same as MapError.

---

## Endpoint: POST api/v1/trips/{id}/complete

### Roles permitidos:
Driver, Admin

### RequireTenant:
No

### Request:
- id: Guid (route)
- Body: CompleteTripRequest (optional) — DistanceKm: decimal?, DurationMinutes: decimal?

### Response (200):
- TripDto

### Posibles errores:
Same as MapError.

---

## Endpoint: POST api/v1/trips/{id}/cancel

### Roles permitidos:
Any authenticated; handler allows passenger, driver of the trip, or Admin/Support.

### RequireTenant:
Yes

### Request:
- id: Guid (route)
- Body: CancelTripRequest (optional) — Reason: string?

### Response (200):
- TripDto

### Posibles errores:
Same as MapError.
