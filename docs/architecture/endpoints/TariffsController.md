# TariffsController

Base route: `api/v1/admin/tariffs`  
Controller: `[Authorize(Roles = "Admin,Support")]`, `[RequireTenant]` on class (all endpoints require tenant).

---

## Endpoint: POST api/v1/admin/tariffs

### Roles permitidos:
Admin, Support

### RequireTenant:
Yes

### Request:
- Body: CreateTariffRequest
  - Name: string (required)
  - Currency: string (required)
  - BaseFare: decimal (required)
  - PricePerKm: decimal (required)
  - PricePerMinute: decimal (required)
  - MinimumFare: decimal (required)
  - Priority: int (default 100)
  - EffectiveFromUtc: DateTime? (nullable)
  - EffectiveUntilUtc: DateTime? (nullable)

### Response (200):
- TariffPlanDto: Id, TenantId, Name, Currency, BaseFare, PricePerKm, PricePerMinute, MinimumFare, IsActive, Priority, EffectiveFromUtc, EffectiveUntilUtc, CreatedAtUtc

### Posibles errores:
- (validation/command errors): 400

---

## Endpoint: PUT api/v1/admin/tariffs/{id}

### Roles permitidos:
Admin, Support

### RequireTenant:
Yes

### Request:
- id: Guid (route)
- Body: UpdateTariffRequest — Name, Currency, BaseFare, PricePerKm, PricePerMinute, MinimumFare, EffectiveFromUtc (nullable)

### Response (200):
- TariffPlanDto

### Posibles errores:
- TARIFF_NOT_FOUND: 404
- TARIFF_CROSS_TENANT: 403
- (other): 400

---

## Endpoint: POST api/v1/admin/tariffs/{id}/activate

### Roles permitidos:
Admin, Support

### RequireTenant:
Yes

### Request:
- id: Guid (route). No body.

### Response (200):
- TariffPlanDto

### Posibles errores:
- TARIFF_NOT_FOUND: 404
- TARIFF_CROSS_TENANT: 403
- TARIFF_PRIORITY_CONFLICT: 400
- (other): 400

---

## Endpoint: GET api/v1/admin/tariffs/active

### Roles permitidos:
Admin, Support

### RequireTenant:
Yes

### Request:
None (tenant from context).

### Response (200):
- TariffPlanDto (the applicable plan at current time for the tenant)

### Posibles errores:
- NO_ACTIVE_TARIFF or NO_APPLICABLE_TARIFF: 404
- (other): 400

---

## Endpoint: GET api/v1/admin/tariffs/fare/quote

### Roles permitidos:
Admin, Support

### RequireTenant:
Yes

### Request:
- Query: distanceKm (decimal), durationMin (decimal)

### Response (200):
- QuoteFareDto: FareAmount (decimal), Currency (string), TariffPlanId (Guid)

### Posibles errores:
- NO_ACTIVE_TARIFF or NO_APPLICABLE_TARIFF: 404
- (other): 400
