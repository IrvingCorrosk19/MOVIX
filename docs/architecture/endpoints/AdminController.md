# AdminController

Base route: `api/v1/admin`  
Controller: `[Authorize(Roles = "Admin,Support")]`. No RequireTenant at class level; tenant scoping applied in query handlers (GetAdminTripsQuery, GetAdminDriversQuery) for list endpoints.

---

## Endpoint: POST api/v1/admin/tenants

### Roles permitidos:
Admin, Support

### RequireTenant:
No

### Request:
- Body: CreateTenantRequest — Name (string)

### Response (200):
- TenantDto: Id, Name, IsActive, CreatedAtUtc

### Posibles errores:
- (validation/duplicate): 400

---

## Endpoint: GET api/v1/admin/tenants

### Roles permitidos:
Admin, Support

### RequireTenant:
No

### Request:
None.

### Response (200):
- List of TenantListItemDto: Id, Name, IsActive

### Posibles errores:
- (handler): 400

---

## Endpoint: GET api/v1/admin/trips

### Roles permitidos:
Admin, Support

### RequireTenant:
No (tenant filter applied in GetAdminTripsQueryHandler via tenant context and ApplyTenantScope).

### Request:
- Query: page (int, default 1), pageSize (int, default 20)

### Response (200):
- AdminTripsResponse: Trips (IReadOnlyList<AdminTripDto>), TotalCount (int)
- AdminTripDto: Id, Status, PassengerId, DriverId (nullable), PickupAddress, DropoffAddress, FinalAmount, CreatedAtUtc

### Posibles errores:
- (e.g. TENANT_REQUIRED from scope): 400

---

## Endpoint: GET api/v1/admin/drivers

### Roles permitidos:
Admin, Support

### RequireTenant:
No (tenant filter in GetAdminDriversQueryHandler).

### Request:
- Query: page (int, default 1), pageSize (int, default 20)

### Response (200):
- List of AdminDriverDto: Id, UserId, Status, IsVerified, CreatedAtUtc

### Posibles errores:
- (e.g. TENANT_REQUIRED): 400

---

## Endpoint: POST api/v1/admin/outbox/{id}/reprocess

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
- InvalidOperationException: 400
