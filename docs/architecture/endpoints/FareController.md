# FareController

Base route: `api/v1/fare`  
Controller: `[Authorize]`, `[RequireTenant]`.

---

## Endpoint: GET api/v1/fare/quote

### Roles permitidos:
Any authenticated user.

### RequireTenant:
Yes

### Request:
- Query: distanceKm (decimal), durationMin (decimal)
- Tenant from ITenantContext (X-Tenant-Id / JWT tenant_id).

### Response (200):
- QuoteFareDto: FareAmount (decimal), Currency (string), TariffPlanId (Guid)

### Posibles errores:
- NO_ACTIVE_TARIFF or NO_APPLICABLE_TARIFF: 404
- (other): 400
