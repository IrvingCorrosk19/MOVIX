# Reporte de Endpoints – Movix API (v1)

Base URL: `/api/v1`  
Autenticación: Bearer JWT (header `Authorization: Bearer <token>`).  
Tenant: header **`X-Tenant-Id`** (Guid) obligatorio en endpoints marcados con `[RequireTenant]`.

---

## 1. Resumen por controlador

| Controlador      | Ruta base           | Auth        | Endpoints |
|------------------|---------------------|------------|-----------|
| AuthController   | `/api/v1/auth`      | No         | 4         |
| AdminController  | `/api/v1/admin`     | Admin,Support | 5      |
| AdminOpsController | `/api/v1/admin/ops` | Admin,Support | 3   |
| TariffsController | `/api/v1/admin/tariffs` | Admin,Support + Tenant | 5 |
| DriversController | `/api/v1/drivers`  | Driver,Admin | 3      |
| FareController   | `/api/v1/fare`      | Cualquier usuario + Tenant | 1 |
| TripsController  | `/api/v1/trips`     | Varios     | 8         |
| PaymentsController | `/api/v1/payments` | Varios     | 3         |

---

## 2. Detalle por endpoint

### 2.1 Auth – `/api/v1/auth`

Sin autenticación. Rate limit: `auth`.

| Método | Ruta | Descripción |
|--------|------|-------------|
| POST | `/register` | Registrar pasajero |
| POST | `/login` | Iniciar sesión |
| POST | `/refresh` | Refrescar token |
| POST | `/logout` | Cerrar sesión |

#### POST `/api/v1/auth/register`

- **Entrada (body):** `RegisterRequest`  
  `{ "email": string, "password": string, "tenantId": "guid" }`
- **Salida:** `202 Accepted` (éxito) | `400` `{ "error", "code" }` (tenant inválido/inactivo, etc.)
- **Orden:** Primero para crear cuenta de pasajero en un tenant.

#### POST `/api/v1/auth/login`

- **Entrada (body):** `LoginRequest`  
  `{ "email": string, "password": string }`
- **Salida:** `200 OK` → `LoginResponse`:  
  `{ "accessToken", "refreshToken", "accessTokenExpiresAtUtc", "expiresInSeconds" }`  
  `401` si credenciales inválidas.
- **Orden:** Después de register (o para Admin/Driver si ya existen); necesario para obtener token.

#### POST `/api/v1/auth/refresh`

- **Entrada (body):** `RefreshRequest`  
  `{ "refreshToken": string }`
- **Salida:** `200 OK` → mismo `LoginResponse` | `401` si refresh inválido.
- **Orden:** Cuando el access token expire.

#### POST `/api/v1/auth/logout`

- **Entrada (body):** `LogoutRequest` (opcional)  
  `{ "refreshToken": string | null }`
- **Salida:** `200 OK`.
- **Orden:** Opcional, al cerrar sesión.

---

### 2.2 Admin – `/api/v1/admin`

Requerido: **JWT** con rol **Admin** o **Support**.

| Método | Ruta | Descripción |
|--------|------|-------------|
| POST | `/tenants` | Crear tenant |
| GET | `/tenants` | Listar tenants |
| GET | `/trips` | Listar viajes (admin) |
| GET | `/drivers` | Listar conductores |
| POST | `/outbox/{id}/reprocess` | Reprocesar dead-letter |

#### POST `/api/v1/admin/tenants`

- **Entrada (body):** `CreateTenantRequest`  
  `{ "name": string }`
- **Salida:** `200 OK` → `TenantDto`:  
  `{ "id", "name", "isActive", "createdAtUtc" }`  
  `400` con `{ "error", "code" }` si falla.
- **Orden:** Típicamente primero (crear tenant antes de registrar usuarios).

#### GET `/api/v1/admin/tenants`

- **Entrada:** ninguna (query vacía).
- **Salida:** `200 OK` → lista de `TenantDto` | `400` con `{ "error" }`.
- **Orden:** Consulta en cualquier momento.

#### GET `/api/v1/admin/trips`

- **Entrada (query):** `page` (default 1), `pageSize` (default 20).
- **Salida:** `200 OK` → `AdminTripsResponse`:  
  `{ "trips": [ { "id", "status", "passengerId", "driverId", "pickupAddress", "dropoffAddress", "finalAmount", "createdAtUtc" } ], "totalCount" }`  
  `400` con `{ "error" }`.
- **Orden:** Consulta cuando se necesite listar viajes.

#### GET `/api/v1/admin/drivers`

- **Entrada (query):** `page` (default 1), `pageSize` (default 20).
- **Salida:** `200 OK` → `AdminDriversResponse`:  
  `{ "drivers": [ { "id", "userId", "status", "isVerified", "createdAtUtc" } ], "totalCount" }`  
  `400` con `{ "error" }`.
- **Orden:** Consulta cuando se necesite listar conductores.

#### POST `/api/v1/admin/outbox/{id}/reprocess`

- **Entrada:** `id` (Guid) en la URL.
- **Salida:** `204 No Content` | `404` | `400` con `{ "error" }`.
- **Orden:** Operación de soporte cuando haya mensajes en dead-letter.

---

### 2.3 Admin Ops – `/api/v1/admin/ops`

Requerido: **JWT** con rol **Admin** o **Support**.

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/payments` | Listar pagos con filtros |
| GET | `/outbox` | Listar mensajes outbox |
| POST | `/outbox/{id}/reprocess` | Reprocesar dead-letter |

#### GET `/api/v1/admin/ops/payments`

- **Entrada (query):** `tenantId?`, `status?` (enum PaymentStatus), `from?`, `to?`, `tripId?`.
- **Salida:** `200 OK` → lista de `PaymentOpsDto`:  
  `{ "id", "tenantId", "tripId", "payerId", "amount", "currency", "status", "externalPaymentId", "createdAtUtc" }`.
- **Orden:** Consulta operativa.

#### GET `/api/v1/admin/ops/outbox`

- **Entrada (query):** `processed?`, `deadletter?`, `from?`, `to?`, `type?`.
- **Salida:** `200 OK` → lista de `OutboxOpsDto`:  
  `{ "id", "eventId", "type", "correlationId", "createdAtUtc", "processedAtUtc", "attemptCount", "isDeadLetter" }`.
- **Orden:** Consulta operativa.

#### POST `/api/v1/admin/ops/outbox/{id}/reprocess`

- **Entrada:** `id` (Guid) en la URL.
- **Salida:** `204 No Content` | `404` | `400` (solo dead-letter).
- **Orden:** Igual que en Admin, operación de soporte.

---

### 2.4 Tariffs (Admin) – `/api/v1/admin/tariffs`

Requerido: **JWT** (Admin/Support) + **header `X-Tenant-Id`**.

| Método | Ruta | Descripción |
|--------|------|-------------|
| POST | (base) | Crear tarifa |
| PUT | `/{id}` | Actualizar tarifa |
| POST | `/{id}/activate` | Activar tarifa |
| GET | `/active` | Obtener tarifa activa |
| GET | `/fare/quote` | Cotizar tarifa (admin) |

#### POST `/api/v1/admin/tariffs`

- **Entrada (body):** `CreateTariffRequest`:  
  `name`, `currency`, `baseFare`, `pricePerKm`, `pricePerMinute`, `minimumFare`, `priority` (default 100), `effectiveFromUtc?`, `effectiveUntilUtc?`.
- **Salida:** `200 OK` → `TariffPlanDto`:  
  `id`, `tenantId`, `name`, `currency`, `baseFare`, `pricePerKm`, `pricePerMinute`, `minimumFare`, `isActive`, `priority`, `effectiveFromUtc`, `effectiveUntilUtc`, `createdAtUtc`.  
  `400` con `{ "error", "code" }`.
- **Orden:** Después de tener tenant; antes de cotizar o crear viajes.

#### PUT `/api/v1/admin/tariffs/{id}`

- **Entrada:** `id` (Guid) en URL; body `UpdateTariffRequest`:  
  `name`, `currency`, `baseFare`, `pricePerKm`, `pricePerMinute`, `minimumFare`, `effectiveFromUtc?`.
- **Salida:** `200 OK` → `TariffPlanDto` | `404` (TARIFF_NOT_FOUND) | `403` (cross-tenant) | `400`.
- **Orden:** Cuando se quiera modificar una tarifa existente.

#### POST `/api/v1/admin/tariffs/{id}/activate`

- **Entrada:** `id` (Guid) en URL.
- **Salida:** `200 OK` → `TariffPlanDto` | `404` | `403` | `400` (p. ej. TARIFF_PRIORITY_CONFLICT).
- **Orden:** Después de crear/actualizar tarifa, para que sea la activa.

#### GET `/api/v1/admin/tariffs/active`

- **Entrada:** ninguna (tenant por header).
- **Salida:** `200 OK` → `TariffPlanDto` | `404` (NO_ACTIVE_TARIFF / NO_APPLICABLE_TARIFF) | `400`.
- **Orden:** Consulta en cualquier momento para el tenant.

#### GET `/api/v1/admin/tariffs/fare/quote`

- **Entrada (query):** `distanceKm`, `durationMin`.
- **Salida:** `200 OK` → mismo formato que Fare quote (ver abajo) | `404` | `400`.
- **Orden:** Igual que Fare quote pero desde contexto admin.

---

### 2.5 Drivers – `/api/v1/drivers`

Requerido: **JWT** con rol **Driver** o **Admin**. Rate limit en status/location: `trips`.

| Método | Ruta | Descripción |
|--------|------|-------------|
| POST | `/onboarding` | Registro de conductor (vehículo) |
| POST | `/status` | Actualizar estado (Available, Busy, etc.) |
| POST | `/location` | Enviar ubicación |

#### POST `/api/v1/drivers/onboarding`

- **Entrada (body):** `DriverOnboardingRequest`:  
  `licenseNumber?`, `vehiclePlate?`, `vehicleModel?`, `vehicleColor?` (todos opcionales).
- **Salida:** `200 OK` → `DriverOnboardingResponse`:  
  `{ "driverId", "isVerified" }`  
  `400` con `{ "error", "code" }` (p. ej. DRIVER_EXISTS, UNAUTHORIZED).
- **Orden:** Una vez por conductor, después de login (usuario con rol Driver).

#### POST `/api/v1/drivers/status`

- **Entrada (body):** `DriverStatusRequest`:  
  `{ "status": "Available" | "Busy" | "Offline" | ... }` (enum `DriverStatus`).
- **Salida:** `200 OK` | `400` con `{ "error", "code" }`.
- **Orden:** Para marcar conductor disponible antes de asignar viajes; actualizar cuando cambie estado.

#### POST `/api/v1/drivers/location`

- **Entrada (body):** `DriverLocationRequest`:  
  `latitude`, `longitude`, `heading?`, `speed?`.
- **Salida:** `200 OK` | `400` con `{ "error", "code" }`.
- **Orden:** Periódicamente o al moverse; útil para asignación y seguimiento.

---

### 2.6 Fare – `/api/v1/fare`

Requerido: **JWT** + **header `X-Tenant-Id`**.

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/quote` | Cotizar tarifa por distancia/duración |

#### GET `/api/v1/fare/quote`

- **Entrada (query):** `distanceKm`, `durationMin`.
- **Salida:** `200 OK` → `QuoteFareDto`:  
  `{ "fareAmount", "currency", "tariffPlanId" }`  
  `404` (NO_ACTIVE_TARIFF / NO_APPLICABLE_TARIFF) | `400`.
- **Orden:** Antes de crear viaje (para mostrar precio estimado al pasajero).

---

### 2.7 Trips – `/api/v1/trips`

Requerido: **JWT** (roles según endpoint). Rate limit en crear/asignar/aceptar/transiciones: `trips`.  
Varios endpoints requieren **header `X-Tenant-Id`** (indicado abajo).

| Método | Ruta | Descripción | Tenant |
|--------|------|-------------|--------|
| POST | (base) | Crear viaje | Sí |
| GET | `/{id}` | Obtener viaje | No |
| POST | `/{id}/assign-driver` | Asignar conductor | Sí, Admin/Support |
| POST | `/{id}/accept` | Conductor acepta viaje | No, Driver/Admin |
| POST | `/{id}/arrive` | Conductor llegó | Sí |
| POST | `/{id}/start` | Iniciar viaje | Sí |
| POST | `/{id}/complete` | Completar viaje | No |
| POST | `/{id}/cancel` | Cancelar viaje | Sí |

#### POST `/api/v1/trips`

- **Entrada (body):** `CreateTripRequest`:  
  `pickupLatitude`, `pickupLongitude`, `dropoffLatitude`, `dropoffLongitude`, `pickupAddress?`, `dropoffAddress?`, `estimatedAmount?`, `currency?` (default "USD").  
  **Header obligatorio:** `Idempotency-Key` (string).
- **Salida:** `200 OK` → `TripDto`:  
  `{ "id", "status", "pickupAddress", "dropoffAddress", "estimatedAmount", "finalAmount", "currency", "createdAtUtc" }`  
  `400` si falta Idempotency-Key o datos inválidos.
- **Orden:** Después de cotizar (fare/quote); lo ejecuta el pasajero (token de pasajero).

#### GET `/api/v1/trips/{id}`

- **Entrada:** `id` (Guid) en URL.
- **Salida:** `200 OK` → `TripDto` | `403` | `404` (TRIP_NOT_FOUND) | `400`.
- **Orden:** En cualquier momento para consultar estado del viaje.

#### POST `/api/v1/trips/{id}/assign-driver`

- **Roles:** Admin, Support. **Header:** `X-Tenant-Id`.
- **Entrada:** `id` (Guid) en URL; sin body.
- **Salida:** `200 OK` → `TripDto` | `403` | `404` | `409` (NO_DRIVERS_AVAILABLE) | `422` (TRIP_INVALID_STATE, etc.).
- **Orden:** Después de crear viaje; asigna un conductor disponible al viaje.

#### POST `/api/v1/trips/{id}/accept`

- **Roles:** Driver, Admin.
- **Entrada:** `id` (Guid) en URL; body `AcceptTripRequest`:  
  `{ "vehicleId": "guid" }`.
- **Salida:** `200 OK` → `TripDto` | `403` | `404` | `422` (invalid transition, etc.).
- **Orden:** Después de assign-driver; el conductor acepta el viaje.

#### POST `/api/v1/trips/{id}/arrive`

- **Roles:** Driver, Admin. **Header:** `X-Tenant-Id`.
- **Entrada:** `id` (Guid) en URL; sin body.
- **Salida:** `200 OK` → `TripDto` | `403` | `404` | `422`.
- **Orden:** Conductor indica que llegó al punto de recogida.

#### POST `/api/v1/trips/{id}/start`

- **Roles:** Driver, Admin. **Header:** `X-Tenant-Id`.
- **Entrada:** `id` (Guid) en URL; sin body.
- **Salida:** `200 OK` → `TripDto` | `403` | `404` | `422`.
- **Orden:** Después de arrive; inicia el trayecto.

#### POST `/api/v1/trips/{id}/complete`

- **Roles:** Driver, Admin.
- **Entrada:** `id` (Guid) en URL; body opcional `CompleteTripRequest`:  
  `{ "distanceKm?", "durationMinutes?" }`.
- **Salida:** `200 OK` → `TripDto` | `403` | `404` | `422`.
- **Orden:** Al finalizar el trayecto; permite registrar distancia/duración real.

#### POST `/api/v1/trips/{id}/cancel`

- **Header:** `X-Tenant-Id`.
- **Entrada:** `id` (Guid) en URL; body opcional `CancelTripRequest`:  
  `{ "reason": string | null }`.
- **Salida:** `200 OK` → `TripDto` | `403` | `404` | `422`.
- **Orden:** Cuando pasajero, conductor o admin cancelen el viaje.

---

### 2.8 Payments – `/api/v1/payments`

Requerido: **JWT** para crear y simular; webhook es **AllowAnonymous**. Rate limit en create: `payments`.

| Método | Ruta | Descripción |
|--------|------|-------------|
| POST | (base) | Crear pago (pasajero paga viaje completado) |
| POST | `/webhook` | Webhook Stripe (sin auth) |
| POST | `/simulate-webhook` | Simular webhook (solo Development) |

#### POST `/api/v1/payments`

- **Entrada (body):** `CreatePaymentRequest`:  
  `{ "tripId", "amount", "currency?" }` (default "USD").  
  **Header obligatorio:** `Idempotency-Key` (string).
- **Salida:** `200 OK` → `PaymentDto`:  
  `{ "id", "tripId", "amount", "currency", "status", "createdAtUtc", "clientSecret?" }`  
  `400` (IDEMPOTENCY_KEY_REQUIRED, TRIP_NOT_COMPLETED, etc.) | `403` | `503` (PAYMENT_GATEWAY_ERROR).
- **Orden:** Después de que el viaje esté **Completed**; lo ejecuta el pasajero (pagador).

#### POST `/api/v1/payments/webhook`

- **Auth:** Ninguna (AllowAnonymous). Body: payload crudo de Stripe. Header: `Stripe-Signature`.
- **Salida:** `200 OK` (procesado o idempotente) | `400` (firma inválida, etc.).
- **Orden:** Llamado por Stripe; no es parte del flujo manual.

#### POST `/api/v1/payments/simulate-webhook`

- **Auth:** Ninguna. Solo si `IsDevelopment()`.
- **Entrada (body):** `SimulateWebhookRequest`:  
  `{ "paymentId?", "eventType?" }`.
- **Salida:** `200 OK` | `404` (env no dev o payment not found) | `400`.
- **Orden:** Pruebas en desarrollo.

---

## 3. Orden de ejecución recomendado (flujo típico)

### 3.1 Setup inicial (una vez)

1. **POST** `/api/v1/auth/login` (Admin) → obtener token.
2. **POST** `/api/v1/admin/tenants` → crear tenant (si no existe).
3. **POST** `/api/v1/admin/tariffs` (con `X-Tenant-Id`) → crear tarifa.
4. **POST** `/api/v1/admin/tariffs/{id}/activate` → activar tarifa.
5. Login Driver (si aplica) → **POST** `/api/v1/auth/login` (Driver).
6. **POST** `/api/v1/drivers/onboarding` → registrar vehículo del conductor.
7. **POST** `/api/v1/drivers/status` con `Available` → conductor disponible.

### 3.2 Flujo por viaje (pasajero)

1. **POST** `/api/v1/auth/register` o **POST** `/api/v1/auth/login` (Pasajero) → token.
2. **GET** `/api/v1/fare/quote?distanceKm=...&durationMin=...` (header `X-Tenant-Id`) → ver precio.
3. **POST** `/api/v1/trips` (body + `Idempotency-Key`, header `X-Tenant-Id`) → crear viaje.
4. **GET** `/api/v1/trips/{id}` → opcional, consultar estado.

### 3.3 Flujo operativo (admin / conductor)

5. **POST** `/api/v1/trips/{id}/assign-driver` (Admin, `X-Tenant-Id`) → asignar conductor.
6. **POST** `/api/v1/trips/{id}/accept` (Driver, body `vehicleId`) → conductor acepta.
7. **POST** `/api/v1/trips/{id}/arrive` (Driver, `X-Tenant-Id`) → conductor llegó.
8. **POST** `/api/v1/trips/{id}/start` (Driver, `X-Tenant-Id`) → inicia viaje.
9. **POST** `/api/v1/trips/{id}/complete` (Driver, body opcional `distanceKm`, `durationMinutes`) → termina viaje.

### 3.4 Pago (pasajero)

10. **POST** `/api/v1/payments` (body `tripId`, `amount`, `currency?`, header `Idempotency-Key`) → crear pago.

### 3.5 Consultas y mantenimiento

- **GET** `/api/v1/admin/trips`, **GET** `/api/v1/admin/drivers` → listados.
- **GET** `/api/v1/admin/tariffs/active` → tarifa activa del tenant.
- **POST** `/api/v1/auth/refresh` → renovar token cuando caduque.

---

## 4. Códigos de error comunes (body `{ "error", "code" }`)

| code | Significado típico |
|------|--------------------|
| TENANT_REQUIRED / TENANT_INVALID | Falta o inválido `X-Tenant-Id` |
| IDEMPOTENCY_KEY_REQUIRED | Falta header Idempotency-Key en trips o payments |
| TRIP_NOT_FOUND | Viaje no existe o sin acceso |
| TRIP_NOT_COMPLETED | Pago sobre viaje no completado |
| TRIP_INVALID_STATE / INVALID_TRANSITION | Transición de estado no permitida |
| NO_ACTIVE_TARIFF / NO_APPLICABLE_TARIFF | Sin tarifa activa para cotizar/crear |
| NO_DRIVERS_AVAILABLE | No hay conductor disponible al asignar |
| PAYMENT_GATEWAY_ERROR | Error del gateway de pago (503) |
| FORBIDDEN / UNAUTHORIZED | Sin permiso o no autenticado |

---

## 5. Headers comunes

| Header | Obligatorio | Uso |
|--------|-------------|-----|
| `Authorization: Bearer <token>` | En todos los endpoints excepto auth y webhooks | JWT |
| `X-Tenant-Id: <guid>` | En endpoints con [RequireTenant] | Tenant de operación |
| `Idempotency-Key: <string>` | POST trips, POST payments | Evitar duplicados |
| `Stripe-Signature` | POST payments/webhook | Validación Stripe |

Documento generado a partir del código de Movix.Api y Movix.Application.
