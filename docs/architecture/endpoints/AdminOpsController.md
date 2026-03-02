# AdminOpsController

Base route: `api/v1/admin/ops`  
Controller: `[Authorize(Roles = "Admin,Support")]`. No RequireTenant; ops endpoints use repository/DB directly with optional tenant filter via query params.

---

## Endpoint: GET api/v1/admin/ops/payments

### Roles permitidos:
Admin, Support

### RequireTenant:
No

### Request:
- Query: tenantId (Guid?), status (string, PaymentStatus), from (DateTime?), to (DateTime?), tripId (Guid?)

### Response (200):
- List of PaymentOpsDto: Id, TenantId (Guid?), TripId, PayerId, Amount, Currency, Status, ExternalPaymentId, CreatedAtUtc

### Posibles errores:
None documented (returns list, possibly empty).

---

## Endpoint: GET api/v1/admin/ops/outbox

### Roles permitidos:
Admin, Support

### RequireTenant:
No

### Request:
- Query: processed (bool?), deadletter (bool?), from (DateTime?), to (DateTime?), type (string?)

### Response (200):
- List of OutboxOpsDto: Id, EventId, Type, CorrelationId, CreatedAtUtc, ProcessedAtUtc, AttemptCount, IsDeadLetter

### Posibles errores:
None documented.

---

## Endpoint: POST api/v1/admin/ops/outbox/{id}/reprocess

### Roles permitidos:
Admin, Support

### RequireTenant:
No

### Request:
- id: Guid (route)

### Response (204):
- No content.

### Posibles errores:
- NotFoundException: 404
- InvalidOperationException: 400 (e.g. "Only dead-letter messages can be reprocessed.")
