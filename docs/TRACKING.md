# MOVIX - Documento de seguimiento (TRACKING)

## Alcance del backend

- API REST para App Pasajero, App Conductor y Panel Administrativo.
- Base de datos: PostgreSQL + PostGIS (movix_core).
- Cache y idempotencia: Redis.
- Autenticación: JWT + refresh token rotativo.
- Preparado para escalado multi-ciudad y multi-país y evolución a microservicios.

## Estado por componente

| Componente | Estado | Notas |
|------------|--------|-------|
| Auth (login/refresh/logout) | Completado | JWT + refresh con detección de reuse |
| Drivers (onboarding/status/location) | Completado | PostGIS para ubicación |
| Trips (CRUD + transiciones) | Completado | Máquina de estados, Idempotency-Key |
| Payments | Completado | Idempotency-Key, estado Pending/Completed |
| Admin (trips/drivers) | Completado | Paginación |
| Health / Ready | Completado | PostgreSQL y Redis |
| Rate limiting | Completado | auth, trips, payments |
| Correlation ID | Completado | Middleware X-Correlation-ID |
| Security headers | Completado | X-Content-Type-Options, X-Frame-Options, etc. |
| Outbox (tabla) | Completado | Tabla outbox_messages; procesador opcional |
| SignalR | Pendiente | Hub para notificaciones en tiempo real |
| OpenTelemetry export | Configurado | Tracing y métricas; export OTLP opcional |

## Deuda técnica y mejoras

- Procesador de outbox (background job) para publicar eventos.
- SignalR Hub para notificaciones a conductores/pasajeros.
- Tests unitarios e integración (Application + API).
- Seed de datos iniciales (usuario Admin, roles).
- Índices espaciales GIST explícitos en migraciones para trips y driver_location_live.

## Criterios de aceptación cubiertos

- Clean Architecture (Domain, Application, Infrastructure, Api).
- CQRS con MediatR y FluentValidation.
- Máquina de estados para Trips (TripStateMachine).
- Concurrencia (RowVersion) en Users, Drivers, Vehicles, Trips, Payments.
- Idempotencia en creación de trips y pagos (Redis + header Idempotency-Key).
- Historial inmutable trip_status_history.
- Auditoría (CreatedAtUtc, UpdatedAtUtc, CreatedBy, UpdatedBy) en entidades.
- UTC en todos los timestamps (timestamptz).
- RBAC y ABAC (ej. GET trip por propietario o Admin/Support).
- No exponer PII en logs.
- Prefijo de logs y métricas: movix.
