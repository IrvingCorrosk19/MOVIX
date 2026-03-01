# AUDITORÍA BACKEND — MOVIX

**Fecha:** 2026-03-01
**Auditor:** Claude Sonnet 4.6 (Software Auditor Senior)
**Rama:** `main`
**Commit:** `f9c77fc` — _Initial commit: MOVIX backend - Clean Architecture, .NET 8, PostgreSQL/PostGIS, Redis, JWT, CQRS_

---

## SECCIÓN 1 — ESTRUCTURA REAL DEL REPOSITORIO

### Árbol de carpetas (archivos fuente)

```
RiderFlow/
├── Movix.sln                          ← Solución principal
├── RiderFlow.sln                      ← SEGUNDO ARCHIVO DE SOLUCIÓN (artefacto — contenido no auditado)
├── README.md
├── docker-compose.yml
├── .gitignore
├── docs/
│   ├── AUDIT.md                       ← Este archivo
│   ├── DECISIONS.md
│   ├── SECURITY.md
│   ├── TECHNICAL-DEBT.md
│   ├── TRACKING.md
│   └── local-db-connection.template.md
├── scripts/
│   └── validate-gates.ps1
├── src/
│   ├── Movix.Api/
│   │   ├── Controllers/V1/
│   │   │   ├── AdminController.cs
│   │   │   ├── AuthController.cs
│   │   │   ├── DriversController.cs
│   │   │   ├── PaymentsController.cs
│   │   │   └── TripsController.cs
│   │   ├── Extensions/SecurityHeadersExtensions.cs
│   │   ├── Middleware/
│   │   │   ├── CorrelationIdMiddleware.cs
│   │   │   └── RequestLoggingMiddleware.cs
│   │   ├── Services/CurrentUserService.cs
│   │   ├── Dockerfile
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   └── appsettings.Development.local.json
│   ├── Movix.Application/
│   │   ├── Admin/
│   │   │   ├── IAdminTripRepository.cs
│   │   │   └── Queries/GetDrivers/, GetTrips/
│   │   ├── Auth/
│   │   │   ├── IAuthService.cs
│   │   │   └── Commands/Login/, Logout/, Refresh/
│   │   ├── Common/
│   │   │   ├── Behaviors/ValidationBehavior.cs
│   │   │   ├── Interfaces/ [ICurrentUserService, IDateTimeService, IIdempotencyService, IUnitOfWork]
│   │   │   └── Models/Result.cs
│   │   ├── Drivers/
│   │   │   ├── IDriverLocationRepository.cs
│   │   │   ├── IDriverRepository.cs
│   │   │   └── Commands/DriverLocation/, DriverOnboarding/, DriverStatus/
│   │   ├── Payments/
│   │   │   ├── IPaymentRepository.cs
│   │   │   └── Commands/CreatePayment/
│   │   ├── Trips/
│   │   │   ├── ITripRepository.cs
│   │   │   ├── Commands/AcceptTrip/, CreateTrip/, TransitionTrip/
│   │   │   └── Queries/GetTrip/
│   │   └── DependencyInjection.cs
│   ├── Movix.Domain/
│   │   ├── Common/ [IAuditableEntity, IConcurrencyEntity]
│   │   ├── Entities/ [Driver, DriverLocationLive, OutboxMessage, Payment, Rating,
│   │   │              RefreshToken, Trip, TripStatusHistory, User, Vehicle]
│   │   ├── Enums/ [DriverStatus, PaymentStatus, Role, TripStatus]
│   │   └── Trip/TripStateMachine.cs
│   └── Movix.Infrastructure/
│       ├── Auth/AuthService.cs         ← incluye clase JwtSettings
│       ├── Migrations/
│       │   ├── 20250301120000_AddSpatialIndexes.cs
│       │   └── AddSpatialIndexes.sql
│       ├── Persistence/
│       │   ├── Configurations/ [10 IEntityTypeConfiguration<T>]
│       │   ├── Interceptors/AuditInterceptor.cs
│       │   ├── Repositories/ [AdminTripRepository, DriverLocationRepository,
│       │   │                  DriverRepository, PaymentRepository, TripRepository]
│       │   ├── DataSeeder.cs
│       │   ├── MovixDbContext.cs
│       │   ├── MovixDbContextFactory.cs
│       │   └── UnitOfWork.cs
│       ├── Services/ [DateTimeService, RedisIdempotencyService]
│       └── DependencyInjection.cs
└── tests/
    └── Movix.Domain.Tests/
        ├── Trip/TripStateMachineTests.cs   ← 5 tests xUnit
        └── Movix.Domain.Tests.csproj
```

### Proyectos en Movix.sln

| Proyecto | Tipo | Framework |
|---|---|---|
| Movix.Domain | Library | net8.0 |
| Movix.Application | Library | net8.0 |
| Movix.Infrastructure | Library | net8.0 |
| Movix.Api | Web | net8.0 |
| Movix.Domain.Tests | Test (xUnit) | net8.0 |

### Principales dependencias

| Proyecto | Dependencias clave |
|---|---|
| Movix.Domain | NetTopologySuite 2.6.0 |
| Movix.Application | MediatR 12.2.0, FluentValidation 11.9.0, NetTopologySuite 2.6.0 |
| Movix.Infrastructure | Npgsql.EF.PostgreSQL 8.0.8, StackExchange.Redis 2.7.33, BCrypt.Net-Next 4.0.3, JwtBearer 8.0.8 |
| Movix.Api | Serilog 8.0.2, OpenTelemetry 1.9.0, HealthChecks (NpgSql + Redis), Swashbuckle 6.5.0 |

---

## SECCIÓN 2 — MÓDULOS IMPLEMENTADOS

| Módulo | Estado | Domain | Application | Infrastructure | Api |
|---|---|---|---|---|---|
| Auth | **Implementado** | User, RefreshToken | Login, Logout, Refresh | AuthService | AuthController |
| Drivers | **Implementado** | Driver, Vehicle, DriverLocationLive | Onboarding, Status, Location | DriverRepository, LocationRepository | DriversController |
| Trips | **Implementado** | Trip, TripStatusHistory, TripStateMachine | CreateTrip, AcceptTrip, TransitionTrip, GetTrip | TripRepository | TripsController |
| Payments | **Parcial** | Payment, PaymentStatus | CreatePayment (solo Pending) | PaymentRepository | PaymentsController |
| Admin | **Implementado (básico)** | — | GetAdminTrips, GetAdminDrivers | AdminTripRepository | AdminController |
| Users/Registro | **NO IMPLEMENTADO** | User (entity only) | — | — | — |
| Ratings | **Parcial — solo BD** | Rating entity | — | RatingConfiguration | — |
| Outbox/Eventos | **Parcial — solo tabla** | OutboxMessage | — | OutboxMessageConfiguration | — |
| Notificaciones (SignalR) | **NO IMPLEMENTADO** | — | — | — | — |

### Casos de uso existentes (13 total)

| Módulo | Caso de uso | Tipo |
|---|---|---|
| Auth | LoginCommand | Command |
| Auth | RefreshTokenCommand | Command |
| Auth | LogoutCommand | Command |
| Drivers | DriverOnboardingCommand | Command |
| Drivers | DriverStatusCommand | Command |
| Drivers | DriverLocationCommand | Command |
| Trips | CreateTripCommand | Command |
| Trips | AcceptTripCommand | Command |
| Trips | TransitionTripCommand | Command |
| Trips | GetTripQuery | Query |
| Payments | CreatePaymentCommand | Command |
| Admin | GetAdminTripsQuery | Query |
| Admin | GetAdminDriversQuery | Query |

---

## SECCIÓN 3 — LISTA COMPLETA DE ENDPOINTS (18 endpoints)

### AUTH

#### POST /api/v1/auth/login
| Propiedad | Valor |
|---|---|
| Finalidad | Autenticar usuario, emitir JWT + refresh token |
| Rol requerido | Público (sin [Authorize]) |
| ABAC | No |
| Idempotency-Key | No |
| Rate Limiting | "auth" — 10 req/min |
| Modifica estado | Sí — crea RefreshToken |
| Transacción | Sí |
| Validaciones | email format + MaxLength(256), password MinLength(8) + MaxLength(100) |
| Concurrencia | No |

```json
// Request
{ "email": "user@example.com", "password": "password123" }

// Response 200
{ "accessToken": "eyJ...", "refreshToken": "base64_token", "accessTokenExpiresAtUtc": "...", "expiresInSeconds": 900 }

// Response 401
{ "error": "Invalid credentials", "code": "INVALID_CREDENTIALS" }
```

---

#### POST /api/v1/auth/refresh
| Propiedad | Valor |
|---|---|
| Finalidad | Rotar refresh token, emitir nuevo par |
| Rol requerido | Público |
| ABAC | No |
| Idempotency-Key | No |
| Rate Limiting | "auth" — 10 req/min |
| Modifica estado | Sí — revoca token anterior, crea nuevo |
| Transacción | Parcial — **2 SaveChanges separados (no atómica)** |
| Validaciones | NotEmpty, MaxLength(500) |
| Concurrencia | No |

```json
// Request
{ "refreshToken": "base64_token" }

// Response 401
{ "error": "Refresh token reuse detected", "code": "REFRESH_TOKEN_REUSE" }
```

---

#### POST /api/v1/auth/logout
| Propiedad | Valor |
|---|---|
| Finalidad | Revocar refresh token activo |
| Rol requerido | **Ninguno — [Authorize] ausente** |
| ABAC | No |
| Idempotency-Key | No |
| Rate Limiting | No |
| Modifica estado | Sí (condicional) |
| Transacción | Sí |
| Validaciones | No |
| Concurrencia | No |

```json
// Request (body opcional)
{ "refreshToken": "base64_token" }

// Response: siempre 200 {}
```

---

### DRIVERS

#### POST /api/v1/drivers/onboarding
| Propiedad | Valor |
|---|---|
| Finalidad | Registrar perfil de conductor + vehículo |
| Rol requerido | Driver, Admin |
| ABAC | Sí — UserId del JWT |
| Idempotency-Key | No |
| Rate Limiting | No |
| Modifica estado | Sí |
| Transacción | Sí |
| Validaciones | LicenseNumber MaxLength(50), Plate MaxLength(20), Model MaxLength(100), Color MaxLength(50) |
| Concurrencia | No |

```json
// Request
{ "licenseNumber": "LIC-123", "vehiclePlate": "ABC-001", "vehicleModel": "Toyota Corolla", "vehicleColor": "White" }

// Response 200
{ "driverId": "uuid", "isVerified": false }
```

**Nota:** `licenseNumber` no se persiste en la entidad Driver — se descarta silenciosamente.

---

#### POST /api/v1/drivers/status
| Propiedad | Valor |
|---|---|
| Finalidad | Cambiar disponibilidad del conductor |
| Rol requerido | Driver, Admin |
| ABAC | Sí — solo el driver del JWT |
| Idempotency-Key | No |
| Rate Limiting | "trips" — 30 req/min |
| Modifica estado | Sí |
| Transacción | Sí |
| Validaciones | IsInEnum() |
| Concurrencia | RowVersion (EF); sin catch de DbUpdateConcurrencyException |

```json
// Request (0=Offline, 1=Online, 2=Busy, 3=OnTrip)
{ "status": 1 }

// Response 200: {}
```

---

#### POST /api/v1/drivers/location
| Propiedad | Valor |
|---|---|
| Finalidad | Registrar ubicación geoespacial en tiempo real |
| Rol requerido | Driver, Admin |
| ABAC | Sí — solo el driver del JWT |
| Idempotency-Key | No |
| Rate Limiting | "trips" — 30 req/min |
| Modifica estado | Sí — insert en driver_location_live |
| Transacción | Sí |
| Validaciones | lat ±90, lng ±180, heading 0..360, speed ≥ 0 |
| Concurrencia | No (insert-only) |

```json
// Request
{ "latitude": 40.7128, "longitude": -74.0060, "heading": 180.0, "speed": 60.5 }

// Response 200: {}
```

---

### TRIPS

#### POST /api/v1/trips
| Propiedad | Valor |
|---|---|
| Finalidad | Crear solicitud de viaje |
| Rol requerido | Cualquier autenticado |
| ABAC | Sí — PassengerId = userId JWT |
| Idempotency-Key | **Requerido** — 400 si ausente |
| Rate Limiting | "trips" — 30 req/min |
| Modifica estado | Sí — crea Trip + TripStatusHistory |
| Transacción | Sí |
| Validaciones | lat/lng bounds, address MaxLength(500), currency Length(3), amount ≥ 0 |
| Concurrencia | Idempotencia Redis (TTL 24h) |

```json
// Request
{
  "pickupLatitude": 40.7128, "pickupLongitude": -74.0060,
  "dropoffLatitude": 40.7580, "dropoffLongitude": -73.9855,
  "pickupAddress": "123 Main St", "dropoffAddress": "456 Park Ave",
  "estimatedAmount": 25.50, "currency": "USD"
}

// Response 200
{ "id": "uuid", "status": "Requested", "pickupAddress": "...", "dropoffAddress": "...", "estimatedAmount": 25.50, "finalAmount": null, "currency": "USD", "createdAtUtc": "..." }
```

---

#### GET /api/v1/trips/{id}
| Propiedad | Valor |
|---|---|
| Finalidad | Obtener detalle de un viaje |
| Rol requerido | Cualquier autenticado |
| ABAC | Sí — pasajero, driver asignado, Admin o Support |
| Idempotency-Key | No |
| Rate Limiting | No |
| Modifica estado | No |
| Transacción | No |
| Validaciones | No |
| Concurrencia | No |

```json
// Response 200
{ "id": "uuid", "status": "Requested", "passengerId": "uuid", "driverId": null, "pickupAddress": "...", "dropoffAddress": "...", "estimatedAmount": 25.50, "finalAmount": null, "currency": "USD", "createdAtUtc": "...", "updatedAtUtc": "..." }

// Response 404: { "error": "Trip not found", "code": "TRIP_NOT_FOUND" }
// Response 400: { "error": "Forbidden", "code": "FORBIDDEN" }  ← debería ser 403
```

---

#### POST /api/v1/trips/{id}/accept
| Propiedad | Valor |
|---|---|
| Finalidad | Driver acepta el viaje (Requested → Accepted) |
| Rol requerido | Driver, Admin |
| ABAC | Sí — driver extraído del JWT, verifica ownership del vehículo |
| Idempotency-Key | No |
| Rate Limiting | "trips" — 30 req/min |
| Modifica estado | Sí — Trip (DriverId, VehicleId, Status) + TripStatusHistory |
| Transacción | Sí |
| Validaciones | TripId NotEmpty, VehicleId NotEmpty |
| Concurrencia | RowVersion; sin catch de DbUpdateConcurrencyException |

```json
// Request
{ "vehicleId": "uuid" }

// Response 400
{ "error": "Invalid transition", "code": "INVALID_TRANSITION" }
```

---

#### POST /api/v1/trips/{id}/arrive
| Propiedad | Valor |
|---|---|
| Finalidad | Driver marca llegada (Accepted → DriverArrived) |
| Rol requerido | Driver, Admin |
| ABAC | **Parcial** — no verifica que el driver JWT sea el asignado |
| Idempotency-Key | No |
| Rate Limiting | No |
| Modifica estado | Sí |
| Transacción | Sí |
| Validaciones | Sí (TripId, TargetStatus) |
| Concurrencia | RowVersion; sin catch |

---

#### POST /api/v1/trips/{id}/start
| Propiedad | Valor |
|---|---|
| Finalidad | Iniciar viaje (DriverArrived → InProgress) |
| Rol requerido | Driver, Admin |
| ABAC | **Parcial** — no verifica ownership |
| Idempotency-Key | No |
| Rate Limiting | No |
| Modifica estado | Sí |
| Transacción | Sí |
| Validaciones | Sí |
| Concurrencia | RowVersion; sin catch |

---

#### POST /api/v1/trips/{id}/complete
| Propiedad | Valor |
|---|---|
| Finalidad | Completar viaje (InProgress → Completed) |
| Rol requerido | Driver, Admin |
| ABAC | **Parcial** — no verifica ownership |
| Idempotency-Key | No |
| Rate Limiting | No |
| Modifica estado | Sí |
| Transacción | Sí |
| Validaciones | Sí |
| Concurrencia | RowVersion; sin catch |

---

#### POST /api/v1/trips/{id}/cancel
| Propiedad | Valor |
|---|---|
| Finalidad | Cancelar viaje desde cualquier estado no terminal |
| Rol requerido | **Cualquier autenticado — sin restricción** |
| ABAC | **NO** — cualquier usuario autenticado puede cancelar cualquier viaje |
| Idempotency-Key | No |
| Rate Limiting | No |
| Modifica estado | Sí |
| Transacción | Sí |
| Validaciones | reason MaxLength(500) |
| Concurrencia | RowVersion; sin catch |

```json
// Request
{ "reason": "Cambié de opinión" }
```

---

### PAYMENTS

#### POST /api/v1/payments
| Propiedad | Valor |
|---|---|
| Finalidad | Registrar intención de pago (siempre en estado Pending) |
| Rol requerido | Cualquier autenticado |
| ABAC | Sí — PayerId = userId JWT |
| Idempotency-Key | **Requerido** — 400 si ausente |
| Rate Limiting | "payments" — 20 req/min |
| Modifica estado | Sí |
| Transacción | Sí |
| Validaciones | Amount > 0, Currency Length(3), TripId NotEmpty |
| Concurrencia | Idempotencia Redis; **no verifica existencia ni estado del Trip** |

```json
// Request
{ "tripId": "uuid", "amount": 25.50, "currency": "USD" }

// Response 200
{ "id": "uuid", "tripId": "uuid", "amount": 25.50, "currency": "USD", "status": "Pending", "createdAtUtc": "..." }
```

---

### ADMIN

#### GET /api/v1/admin/trips
| Propiedad | Valor |
|---|---|
| Finalidad | Listar viajes paginados |
| Rol requerido | Admin, Support |
| ABAC | No |
| Query params | `?page=1&pageSize=20` |
| Modifica estado | No |
| Validaciones | No |
| Concurrencia | No |

```json
// Response 200
{ "trips": [{ "id": "...", "status": "Requested", "passengerId": "...", "driverId": null, "pickupAddress": "...", "dropoffAddress": "...", "finalAmount": null, "createdAtUtc": "..." }], "totalCount": 150 }
```

---

#### GET /api/v1/admin/drivers
| Propiedad | Valor |
|---|---|
| Finalidad | Listar conductores paginados |
| Rol requerido | Admin, Support |
| ABAC | No |
| Query params | `?page=1&pageSize=20` |
| Modifica estado | No |
| Validaciones | No |
| Concurrencia | No |

```json
// Response 200
{ "drivers": [{ "id": "...", "userId": "...", "status": "Online", "isVerified": true, "createdAtUtc": "..." }], "totalCount": 42 }
```

---

### SISTEMA

| Endpoint | Finalidad | Auth | Checks |
|---|---|---|---|
| `GET /health` | Estado completo de salud del sistema | Público | postgres + redis |
| `GET /ready` | Readiness probe (infra lista) | Público | postgres + redis (tag "ready") |
| `GET /swagger` | Documentación OpenAPI interactiva | Público | — |

---

## SECCIÓN 4 — MÁQUINA DE ESTADOS DE TRIPS

### Estados definidos

| Estado | Int | Terminal |
|---|---|---|
| `Requested` | 0 | No |
| `Accepted` | 1 | No |
| `DriverArrived` | 2 | No |
| `InProgress` | 3 | No |
| `Completed` | 4 | **Sí** |
| `Cancelled` | 5 | **Sí** |

### Transiciones permitidas (TripStateMachine.AllowedTransitions)

```
Requested     ──→ Accepted
              └─→ Cancelled

Accepted      ──→ DriverArrived
              └─→ Cancelled

DriverArrived ──→ InProgress
              └─→ Cancelled

InProgress    ──→ Completed
              └─→ Cancelled

Completed     ──→ (ninguna — terminal)
Cancelled     ──→ (ninguna — terminal)
```

### Validación aplicada

- `TripStateMachine.CanTransition(from, to)` → `false` si: `from == to`, transición no listada, o estado origen desconocido
- Invocado en `AcceptTripCommandHandler` (hardcoded a Accepted) y `TransitionTripCommandHandler` (genérico)
- Resultado negativo → `Result.Failure("INVALID_TRANSITION")` → HTTP 400

### Cómo se bloquean estados inválidos

```
Request HTTP → Handler → TripStateMachine.CanTransition()
                              ↓ false
                    Result.Failure("INVALID_TRANSITION")
                              ↓
                    Controller → 400 BadRequest
                    (SaveChanges NO se llama)
```

### Historial de estados

- Cada transición inserta en `trip_status_history` (solo insert, nunca update)
- **Inconsistencia**: el entry inicial en `CreateTripCommandHandler` registra `FromStatus = Requested` y `ToStatus = Requested`, semánticamente incorrecto

### Tests

5 tests unitarios xUnit en `Movix.Domain.Tests/Trip/TripStateMachineTests.cs`:
- Requested → Accepted (true)
- Requested → Cancelled (true)
- Requested → InProgress (false)
- Completed → Cancelled (false)
- mismo estado (false)

---

## SECCIÓN 5 — SEGURIDAD

_Ver `docs/SECURITY.md` para análisis completo._

**Resumen:**
- JWT Bearer HS256, access token 15 min, refresh token 7 días (hashed SHA-256 en DB)
- Refresh token rotation + reuse detection implementados
- Rate limiting in-process (no distribuido): auth 10/min, trips 30/min, payments 20/min
- Correlation ID via middleware
- Security headers: nosniff, DENY frames, XSS protection, Referrer-Policy
- **Faltan**: CSP, HSTS, ABAC completo en transiciones, cancel sin ABAC

---

## SECCIÓN 6 — BASE DE DATOS

### Tablas existentes (10 + EF system)

| Tabla | Entidad | RowVersion | Audit |
|---|---|---|---|
| users | User | Sí | Sí |
| refresh_tokens | RefreshToken | No | Sí |
| drivers | Driver | Sí | Sí |
| vehicles | Vehicle | Sí | Sí |
| trips | Trip | Sí | Sí |
| trip_status_history | TripStatusHistory | No | Sí |
| driver_location_live | DriverLocationLive | No | Sí |
| payments | Payment | Sí | Sí |
| ratings | Rating | No | Sí |
| outbox_messages | OutboxMessage | No | No |

### Índices GIST espaciales

| Tabla | Columna | Tipo |
|---|---|---|
| trips | PickupLocation | GIST |
| trips | DropoffLocation | GIST |
| driver_location_live | Location | GIST |

### Concurrencia

- Optimistic concurrency via `RowVersion` (`byte[]`, `IsRowVersion()`) en: User, Driver, Vehicle, Trip, Payment
- **Sin manejo de `DbUpdateConcurrencyException`** en ningún handler → HTTP 500 en conflictos

### PostGIS

- **Sí habilitado** — imagen `postgis/postgis:16-3.4-alpine`
- `CREATE EXTENSION IF NOT EXISTS postgis` en migración
- `UseNetTopologySuite()` en EF Core
- Tipo de geometría: `Point` con SRID 4326 (WGS84)

---

## SECCIÓN 7 — OBSERVABILIDAD

### Logging (Serilog)

- Sink: Console con template estructurado
- Enrichers: `FromLogContext()`, `WithProperty("Application", "movix-api")`
- Correlation ID inyectado por `RequestLoggingMiddleware`
- Sin log de body, credenciales ni PII
- **Export OTLP para logs: NO configurado**

### Tracing (OpenTelemetry)

- Instrumentación: `AddAspNetCoreInstrumentation()`, `AddHttpClientInstrumentation()`
- Resource: "movix-api"
- **Endpoint OTLP: NO configurado en appsettings**

### Health Checks

| Endpoint | Checks | Respuesta |
|---|---|---|
| `/health` | postgres + redis | JSON con detalle de cada check |
| `/ready` | postgres + redis (tag "ready") | JSON con detalle |

### Métricas

- `AddAspNetCoreInstrumentation()` habilitado
- **Sin métricas de negocio personalizadas**
- **Exportador: NO configurado**

---

## SECCIÓN 8 — DEUDAS TÉCNICAS

_Ver `docs/TECHNICAL-DEBT.md` para el listado completo con clasificación y prioridad._

**Resumen por categoría:**

| Categoría | Cantidad |
|---|---|
| Riesgos de seguridad | 4 |
| Bugs lógicos | 5 |
| Funcionalidades incompletas | 6 |
| Inconsistencias arquitectónicas | 5 |
| Concurrencia | 1 |
| Observabilidad | 3 |

---

_Fin del documento de auditoría._
