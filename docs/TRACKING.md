# MOVIX - Documento de seguimiento (TRACKING)

## Alcance del backend

- API REST para App Pasajero, App Conductor y Panel Administrativo.
- Base de datos: PostgreSQL + PostGIS (movix_core).
- Cache y idempotencia: Redis.
- Autenticación: JWT + refresh token rotativo.
- Preparado para escalado multi-ciudad y multi-país y evolución a microservicios.

---

## Estado por componente

_Actualizado con hallazgos de auditoría — 2026-03-01_

| Componente | Estado | Notas |
|------------|--------|-------|
| Auth (login/refresh/logout) | **Completado — Robusto** | JWT + refresh rotativo con `FamilyId`, invalidación completa de familia en reuse, atómico (single SaveChanges), auditable (`RevocationReason`) — 2 tests de integración con EF InMemory |
| Drivers (onboarding/status/location) | Completado | PostGIS para ubicación. LicenseNumber no persiste (ver TECHNICAL-DEBT B-5) |
| Trips (CRUD + transiciones) | Completado | Máquina de estados, Idempotency-Key |
| Driver Matching (assign-driver MVP) | **Completado** | DriverAvailability (IsOnline, CurrentTripId, RowVersion), AssignDriverCommand, POST /api/v1/trips/{id}/assign-driver. FIFO matching. Concurrencia vía RowVersion (NO_DRIVERS_AVAILABLE). Sync: onboarding crea fila; DriverStatus actualiza IsOnline; TransitionTrip limpia CurrentTripId al completar/cancelar. |
| Payments | **Parcial — B-3 Hardened** | Solo estado Pending. Sin pasarela de pago real. CreatePaymentCommandHandler valida Trip existente, Completed y Passenger (ver TECHNICAL-DEBT B-3 resuelto; F-4 pendiente) |
| Admin (trips/drivers) | Completado | Paginación. Sin gestión de usuarios ni acciones de moderación |
| Users / Registro | **Completado** | POST /api/v1/auth/register. Anti-enumeration (202 Accepted). BCrypt, Rol Passenger, Outbox UserRegistered. Validator: min 8, 1 mayúscula, 1 número. |
| Ratings | **Parcial — solo BD** | Tabla y configuración EF. Sin handlers ni endpoints |
| Outbox (tabla) | **Completado — F-2 + Worker** | OutboxHostedService (BackgroundService) + OutboxProcessor, IEventPublisher. Config: Outbox:Enabled, PollIntervalMs, MaxBatchSize, ErrorBackoffMs. Retry/backoff/dead-letter, FIFO, sin PII en logs. HealthCheck outbox en /health. Tests: processor + HostedService (DI scope). |
| Health / Ready | Completado | PostgreSQL y Redis |
| Rate limiting | Completado (in-process) | auth 10/min, trips 30/min, payments 20/min — no distribuido |
| Correlation ID | Completado | Middleware X-Correlation-ID |
| Security headers | Completado | X-Content-Type-Options, X-Frame-Options, X-XSS-Protection, Referrer-Policy |
| Índices GIST espaciales | Completado | trips.PickupLocation, trips.DropoffLocation, driver_location_live.Location |
| SignalR | Pendiente | Hub para notificaciones en tiempo real |
| OpenTelemetry export | Configurado (sin endpoint) | Tracing y métricas configurados; endpoint OTLP no configurado en appsettings |
| Seed (Development) | Completado | Admin (ADMIN_EMAIL, ADMIN_PASSWORD); opcional Driver+Vehicle (DRIVER_EMAIL, DRIVER_PASSWORD) |
| ABAC (transiciones de viaje) | **Completado** | arrive/start/complete: solo driver asignado o Admin/Support. cancel: pasajero, driver o Admin/Support. HTTP 403 correcto. |
| Pricing Engine / Tariffs | **Completado — Tenant-safe** | TariffPlan tenant-ready, IFareCalculator, Create/Update/Activate/GetActive/QuoteFare. Snapshot en Trip al completar. Rutas admin: api/v1/admin/tariffs (tenant solo desde X-Tenant-Id). RequireTenant en TariffsController y FareController. Cross-tenant bloqueado (TARIFF_CROSS_TENANT → 403). X-Tenant-Id inválido → 400 TENANT_INVALID. |
| Tenancy MVP | **Completado** | Entidad Tenant. Tabla tenants, FK trips/tariff_plans. ITenantContext (header X-Tenant-Id). RequireTenantAttribute → 400 TENANT_REQUIRED. POST/GET /api/v1/admin/tenants. CreateTrip/TransitionTrip/GET fare/quote exigen tenant; Trip.TenantId persistido. |

---

## Deuda técnica y mejoras

- ~~Procesador de outbox (background job) para publicar eventos.~~ Completado F-2: OutboxProcessor, IEventPublisher, inserciones TripCompleted/PaymentCreated/DriverAssigned/UserRegistered, AddAsync en IOutboxMessageRepository, tests.
- SignalR Hub para notificaciones a conductores/pasajeros.
- ~~Tests unitarios e integración.~~ 68 tests: Domain (5), Application (31), Infrastructure (23), Api (9). Pendiente: tests de integración de API.
- ~~Seed de datos iniciales (usuario Admin, roles).~~ Completado: DataSeeder en Infrastructure; Admin + opcional Driver+Vehicle; variables de entorno, solo en Development.
- ~~Índices espaciales GIST explícitos en migraciones para trips y driver_location_live.~~ Completado: migración AddSpatialIndexes (trips.PickupLocation, trips.DropoffLocation, driver_location_live.Location).
- ~~ABAC completo en transiciones de viaje.~~ Completado: 2026-03-01 — TransitionTripCommandHandler + MapError en TripsController + 5 tests unitarios.
- Endpoint de registro de usuarios (`POST /api/v1/auth/register`).
- Integración con pasarela de pago real.

Ver `docs/TECHNICAL-DEBT.md` para el listado completo con clasificación y prioridades.

---

## Criterios de aceptación cubiertos

- Clean Architecture (Domain, Application, Infrastructure, Api).
- CQRS con MediatR y FluentValidation.
- Máquina de estados para Trips (TripStateMachine) — 5 tests unitarios xUnit.
- Concurrencia (RowVersion) en Users, Drivers, Vehicles, Trips, Payments.
- Idempotencia en creación de trips y pagos (Redis + header Idempotency-Key, TTL 24h).
- Historial inmutable `trip_status_history`.
- Auditoría (CreatedAtUtc, UpdatedAtUtc, CreatedBy, UpdatedBy) en todas las entidades excepto OutboxMessage.
- UTC en todos los timestamps (timestamptz).
- RBAC (roles: Passenger, Driver, Admin, Support).
- ABAC completo — GetTrip, AcceptTrip, arrive/start/complete (driver asignado o Admin/Support), cancel (pasajero, driver o Admin/Support). HTTP 403 correcto vía `MapError()`.
- No exponer PII en logs.
- Prefijo de logs y métricas: movix.

---

## Backend Ready — Gates

| Gate | Estado | Verificación |
|------|--------|--------------|
| 1) dotnet build Release | OK | `dotnet build Movix.sln -c Release` |
| 2) dotnet test | OK | `dotnet test Movix.sln -c Release` — 68 tests: Domain (5) + Application (31) + Infrastructure (23) + Api (9) |
| 3) docker-compose up | Requiere Docker | `docker-compose up -d --build` |
| 4) Migraciones EF + PostGIS | OK en código | Migración Initial + AddSpatialIndexes; aplicadas al arranque en Program.cs |
| 5) /health y /ready | Requiere Docker | GET :8080/health, GET :8080/ready |
| 6) No PII en logs | OK | RequestLoggingMiddleware solo scope CorrelationId; sin log de body/credenciales |
| 7) Idempotency-Key enforced | OK | TripsController y PaymentsController devuelven 400 si header ausente |
| 8) Refresh rotation + reuse | **OK — Robusto** | FamilyId, rotación atómica (single SaveChanges), invalidación completa de familia en reuse. 2 tests con EF InMemory — 2026-03-01 |

---

## Próximos pasos sugeridos (por prioridad)

| Prioridad | Tarea |
|-----------|-------|
| ~~🔴 Crítico~~ | ~~ABAC en arrive/start/complete~~ — **Resuelto 2026-03-01** |
| ~~🔴 Crítico~~ | ~~ABAC en cancel~~ — **Resuelto 2026-03-01** |
| ~~🔴 Crítico~~ | ~~Endpoint `POST /api/v1/auth/register`~~ — **Resuelto** |
| ~~🔴 Crítico~~ | ~~Corregir doble `SaveChanges` en `RefreshTokenAsync`~~ — **Resuelto 2026-03-01** |
| ~~🔴 Crítico~~ | ~~Procesador de Outbox + inserciones de eventos en handlers~~ — **Resuelto F-2** |
| 🟠 Alto | Externalizar JWT SecretKey a gestor de secretos |
| ~~🟠 Alto~~ | ~~Manejar `DbUpdateConcurrencyException` → HTTP 409 en todos los handlers~~ — **Resuelto 2026-03-01** |
| 🟠 Alto | Integrar pasarela de pago real + transiciones de PaymentStatus |
| 🟠 Alto | Implementar módulo Ratings (handlers + endpoints) |
| 🟠 Alto | Configurar endpoint OTLP para OpenTelemetry |
| 🟠 Alto | Implementar SignalR Hub |
| 🟠 Alto | Tests de integración (Application + API) |
| 🟡 Medio | Agregar propiedad `LicenseNumber` a entidad Driver |
| 🟡 Medio | Corregir HTTP 400→403 en GetTripQueryHandler |
| ~~🟡 Medio~~ | ~~Validar existencia y estado del Trip en CreatePaymentCommandHandler~~ — **Resuelto (B-3 Hardening)** |
| 🟡 Medio | Migrar rate limiter a Redis para soporte multi-instancia |
| 🟡 Medio | Agregar headers CSP y HSTS |
