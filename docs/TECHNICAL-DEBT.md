# MOVIX — Deudas Técnicas

**Generado:** 2026-03-01
**Fuente:** Auditoría de código — commit `f9c77fc`

Leyenda de prioridad: 🔴 Crítico | 🟠 Alto | 🟡 Medio | 🟢 Bajo

---

## RIESGOS DE SEGURIDAD

### ~~R-1~~ ✅ RESUELTO — ABAC en transiciones de viaje (arrive / start / complete)

**Resuelto:** 2026-03-01
**Archivos modificados:**
- `src/Movix.Application/Trips/Commands/TransitionTrip/TransitionTripCommandHandler.cs`
- `src/Movix.Api/Controllers/V1/TripsController.cs`
- `tests/Movix.Application.Tests/Trips/TransitionTripCommandHandlerTests.cs`

**Solución aplicada:**
- `DriverArrived | InProgress | Completed`: requiere `trip.DriverId == userId` o `role in [Admin, Support]`. Si `trip.DriverId == null` → `DRIVER_NOT_ASSIGNED` (HTTP 422). Si userId no coincide → `FORBIDDEN` (HTTP 403).
- `TripsController.MapError()` centraliza el mapeo de `ErrorCode` a códigos HTTP semánticos correctos.
- 5 tests unitarios con Moq cubren todos los casos de autorización.

---

### ~~R-2~~ ✅ RESUELTO — Control de acceso en Cancel

**Resuelto:** 2026-03-01
**Archivos modificados:**
- `src/Movix.Application/Trips/Commands/TransitionTrip/TransitionTripCommandHandler.cs`
- `tests/Movix.Application.Tests/Trips/TransitionTripCommandHandlerTests.cs`

**Solución aplicada:**
- `Cancelled`: requiere `userId == trip.PassengerId OR trip.DriverId OR role in [Admin, Support]`. Si no cumple → `FORBIDDEN` (HTTP 403).
- Tests: `Cancel_WhenRandomUser_Returns_FORBIDDEN` y `Cancel_WhenPassengerIsOwner_Succeeds`.

---

### ~~R-3~~ ✅ RESUELTO — JWT SecretKey externalizado y validado

**Resuelto:** 2026-03-01
**Archivos modificados:**
- src/Movix.Api/appsettings.json
- docker-compose.yml
- src/Movix.Api/Program.cs
- tests/Movix.Api.Tests/Startup/JwtSecretKeyValidationTests.cs

**Solución aplicada:**
- Eliminado SecretKey de appsettings.json
- Eliminado default en docker-compose.yml
- Validación fail-fast en Program.cs:
  * no null
  * no vacío
  * mínimo 32 caracteres
  * no contener "CHANGE_ME"
- Sin fallback
- Tests de integración con WebApplicationFactory

---

### ~~R-4~~ ✅ RESUELTO — Reuse detection no invalida familia completa de tokens

**Resuelto:** 2026-03-01
**Archivos modificados:** (ver B-1 — mismos archivos)

**Solución aplicada:**
- Añadido `FamilyId` (Guid, NOT NULL) a entidad `RefreshToken`
- Añadido `RevocationReason` (string?, max 100) para auditoría
- EF configuration actualizada: `FamilyId` requerido + índice `IX_refresh_tokens_FamilyId`
- Migración EF: `AddRefreshTokenFamily` — schema completo con columnas nuevas
- `RefreshTokenAsync` ahora consulta SIN filtro `RevokedAtUtc == null`
  - Si el token encontrado tiene `RevokedAtUtc != null` → reuse → `RevokeTokenFamilyAsync` revoca TODOS los tokens activos con mismo `FamilyId` → único `SaveChanges` → `REFRESH_TOKEN_REUSE`
  - `RevokeTokenFamilyAsync` marca `RevocationReason = "Reuse detected"` en todos
- Test: `Refresh_WhenReuseDetected_RevokesEntireFamily` verifica invalidación completa de familia

---

## BUGS LÓGICOS

### ~~B-1~~ ✅ RESUELTO — Doble SaveChangesAsync no atómica en RefreshTokenAsync

**Resuelto:** 2026-03-01
**Archivos modificados:**
- `src/Movix.Domain/Entities/RefreshToken.cs`
- `src/Movix.Infrastructure/Persistence/Configurations/RefreshTokenConfiguration.cs`
- `src/Movix.Infrastructure/Auth/AuthService.cs`
- `src/Movix.Infrastructure/Persistence/Migrations/20260301181604_AddRefreshTokenFamily.cs`
- `tests/Movix.Infrastructure.Tests/Auth/AuthServiceTests.cs` (CREADO)

**Solución aplicada:**
- Eliminado `CreateRefreshTokenAsync` (que tenía el `SaveChanges` interno)
- Introducido `BuildRefreshToken(userId, familyId)` — sincrónico, solo construye la entidad, sin save
- `LoginAsync`: genera `FamilyId = Guid.NewGuid()`, llama `BuildRefreshToken`, hace `_db.Add`, único `SaveChanges`
- `RefreshTokenAsync`: marca token anterior como `Rotated`, agrega nuevo token con mismo `FamilyId`, único `SaveChanges` al final
- Test: `Refresh_NormalRotation_Succeeds_WithSameFamilyId` verifica rotación atómica con EF InMemory

---

### B-2 🟠 HTTP 400 en lugar de 403 para acceso denegado

**Archivo:** `src/Movix.Application/Trips/Queries/GetTrip/GetTripQueryHandler.cs`

Cuando el usuario no tiene permiso para ver un viaje, el handler retorna:
```csharp
Result<TripDetailDto>.Failure("Forbidden", "FORBIDDEN")
```
El controller mapea `!result.Succeeded` a `return NotFound(...)` o `return BadRequest(...)` — en este caso `BadRequest` (HTTP 400).

**Impacto:** Los clientes no pueden distinguir "recurso no encontrado" de "acceso denegado". Viola la semántica HTTP.

**Corrección:** El controller debe evaluar el `ErrorCode` y retornar `403 Forbidden` cuando sea `"FORBIDDEN"`.

---

### ~~B-3~~ ✅ RESUELTO — Payment no verifica existencia ni estado del Trip (Hardening)

**Resuelto:** 2026-03-01
**Archivos modificados:**
- `src/Movix.Application/Payments/Commands/CreatePayment/CreatePaymentCommandHandler.cs`
- `tests/Movix.Infrastructure.Tests/Payments/CreatePaymentCommandHandlerTests.cs` (nuevo)

**Solución aplicada:**
- Handler inyecta `ITripRepository`; antes de crear el pago: carga el trip por `request.TripId`.
- Si trip es null → `Result.Failure("Trip not found", "TRIP_NOT_FOUND")`.
- Si `trip.Status != Completed` → `Result.Failure("Trip not completed", "TRIP_NOT_COMPLETED")`.
- Si `trip.PassengerId != currentUserId` → `Result.Failure("Unauthorized payment attempt", "UNAUTHORIZED_PAYMENT")`.
- Un único SaveChanges; no se lanzan excepciones por FK.
- 4 tests en Infrastructure.Tests: Trip inexistente, Trip no Completed, usuario no Passenger, caso válido → Success.

---

### B-4 🟡 Primer entry de StatusHistory semánticamente incorrecto

**Archivo:** `src/Movix.Application/Trips/Commands/CreateTrip/CreateTripCommandHandler.cs`

Al crear un viaje, se inserta en `trip_status_history`:
```csharp
FromStatus = TripStatus.Requested,
ToStatus   = TripStatus.Requested
```

La creación no es una transición de estado; `FromStatus` debería ser `null` o no existir ese registro inicial.

**Impacto:** Historial de estados incorrecto; queries de analítica que sumen transiciones contarán la creación como una transición.

**Corrección:** Omitir el insert inicial o usar un evento "Created" separado con `FromStatus = null`.

---

### B-5 🟡 LicenseNumber no persistido en Driver

**Archivo:** `src/Movix.Application/Drivers/Commands/DriverOnboarding/DriverOnboardingCommandHandler.cs`

`DriverOnboardingCommand` recibe `LicenseNumber` como parámetro, pero la entidad `Driver` no tiene esa propiedad. El valor se descarta silenciosamente sin error, warning ni log.

**Impacto:** Dato de cumplimiento regulatorio (número de licencia del conductor) se pierde en cada onboarding.

**Corrección:** Agregar propiedad `LicenseNumber` a la entidad `Driver` y su configuración EF.

---

## FUNCIONALIDADES INCOMPLETAS

### ~~F-1~~ ✅ RESUELTO — User registration

**Resuelto:** Endpoint `POST /api/v1/auth/register` implementado. Anti-enumeration (siempre 202 Accepted). BCrypt, Rol Passenger por defecto, OutboxMessage Type="UserRegistered" con payload { userId, email, role, occurredAtUtc }. Validator: contraseña min 8, al menos 1 mayúscula, 1 número. Un solo SaveChanges (User + Outbox). Tests: RegisterTests (Infrastructure): nuevo usuario crea user + outbox; email existente no crea duplicado ni outbox.

**Entidades afectadas:** User, OutboxMessage

---

### F-2 🔴 Outbox sin procesador ni inserciones

La tabla `outbox_messages` existe y tiene configuración EF, pero:
- Ningún handler inserta mensajes en ella
- No existe ningún `BackgroundService` o `IHostedService` que la procese
- No hay lógica de publicación a ningún bus de mensajes

**Impacto:** El patrón Outbox está completamente inoperativo. Cambios de estado en trips/payments no generan eventos. Sin eventos, no hay posibilidad de integración con otros servicios o notificaciones.

---

### F-3 🟠 Módulo Ratings sin Application ni API

La entidad `Rating` tiene tabla y configuración en BD, pero:
- No existe ningún Command handler para crear ratings
- No existe ningún Query handler para consultarlos
- No existe ningún endpoint HTTP

**Impacto:** Los ratings nunca se crean; la tabla permanece vacía.

---

### F-4 🟠 Pagos siempre en estado Pending

`CreatePaymentCommand` solo crea pagos con `PaymentStatus.Pending`. No existe lógica para:
- Transicionar a `Completed` (no hay pasarela de pago integrada)
- Transicionar a `Failed`
- Manejar `Refunded`
- Establecer `ExternalPaymentId`

**Impacto:** El módulo de pagos es un stub. No hay cobros reales.

---

### F-5 🟠 SignalR no implementado

Notificaciones en tiempo real documentadas como pendientes en TRACKING.md. Sin esto, los clientes (App Pasajero, App Conductor) deben hacer polling para conocer cambios de estado.

**Afecta:** Experiencia de usuario, eficiencia de red, latencia percibida.

---

### F-6 🟡 Admin sin gestión de usuarios

`AdminController` solo tiene GET de trips y drivers. No existe ningún endpoint para:
- Suspender/activar usuarios
- Cambiar roles
- Gestionar verificación de drivers
- Ver logs de auditoría
- Gestión de vehículos

---

## INCONSISTENCIAS ARQUITECTÓNICAS

### A-1 🟠 Doble archivo de solución

Existen `Movix.sln` y `RiderFlow.sln` en la raíz. El contenido de `RiderFlow.sln` no fue verificado. Puede causar confusión en CI/CD y al abrir el proyecto.

**Corrección:** Eliminar el archivo sobrante o documentar su propósito.

---

### A-2 🟡 Rate limiter in-process — no escalable horizontalmente

El rate limiter usa la implementación in-process de ASP.NET Core (Fixed Window). Los contadores no se comparten entre instancias. ADR-008 lo documenta pero no hay solución implementada.

**Corrección:** Migrar a un rate limiter con backing store en Redis (ej. `AspNetCore.RateLimit` con Redis, o implementación custom).

---

### A-3 🟡 OTLP export no configurado

Los paquetes OpenTelemetry están instalados y registrados, pero el endpoint OTLP no está en `appsettings.json`. Traces y métricas se producen internamente pero no se exportan a ningún destino (Jaeger, Grafana Tempo, Prometheus, etc.).

**Corrección:** Agregar sección `OpenTelemetry:Endpoint` en appsettings y configurar `AddOtlpExporter()`.

---

### A-4 🟡 JwtSettings definida dentro de AuthService.cs

La clase `JwtSettings` está al final del archivo `AuthService.cs` en lugar de tener su propio archivo, violando la convención de un tipo por archivo y dificultando la navegación.

---

### A-5 🟢 appsettings.Development.json apunta a hosts Docker

`Host=postgres` y `redis:6379` son nombres de servicios Docker. Un desarrollador corriendo sin Docker necesita sobreescribir via `appsettings.Development.local.json` — dependencia implícita no documentada en onboarding.

---

## PROBLEMAS DE CONCURRENCIA

### ~~C-1~~ ✅ RESUELTO — DbUpdateConcurrencyException no manejada

**Resuelto:** 2026-03-01
**Archivos modificados:**
- `src/Movix.Application/Common/Exceptions/ConcurrencyException.cs` (CREADO)
- `src/Movix.Infrastructure/Persistence/UnitOfWork.cs`
- `src/Movix.Application/Trips/Commands/TransitionTrip/TransitionTripCommandHandler.cs`
- `src/Movix.Application/Trips/Commands/AcceptTrip/AcceptTripCommandHandler.cs`
- `src/Movix.Application/Drivers/Commands/DriverStatus/DriverStatusCommandHandler.cs`
- `tests/Movix.Application.Tests/Trips/TransitionTripCommandHandlerTests.cs`

**Solución aplicada (Clean Architecture):**
- `ConcurrencyException` creada en Application layer (sin dependencia de EF)
- `UnitOfWork` en Infrastructure captura `DbUpdateConcurrencyException` y relanza como `ConcurrencyException`
- Los tres handlers mutantes capturan `ConcurrencyException` → `Result.Failure("Concurrent modification", "CONFLICT")`
- `TripsController.MapError()` mapea `CONFLICT` → HTTP 409
- `CreatePaymentCommandHandler` excluido (insert puro, no aplica optimistic concurrency)
- Test unitario: `Arrive_WhenConcurrentModification_Returns_CONFLICT` verifica el flujo completo

---

## COBERTURA DE TESTS

### T-1 🟠 Cobertura de tests parcial

**Cubierto (2026-03-01):** 5 tests de `TripStateMachine` (Domain) + 6 tests de `TransitionTripCommandHandler` ABAC + concurrencia (Application) + 2 tests de `AuthService` reuse + rotación (Infrastructure). Total: **13 tests**.

**Pendiente:**
- Infrastructure repositories
- API endpoints (integration tests)
- AuthService (refresh, reuse detection)
- Handlers: AcceptTrip, CreateTrip, CreatePayment

**Impacto:** Los bugs documentados (B-1, B-3, B-5) no tienen cobertura automática.

---

## RESUMEN DE PRIORIDAD

| ID | Descripción | Prioridad |
|---|---|---|
| ~~R-1~~ | ~~ABAC ausente en transiciones~~ | ✅ Resuelto 2026-03-01 |
| ~~R-2~~ | ~~Cancel sin control de acceso~~ | ✅ Resuelto 2026-03-01 |
| ~~B-2~~ | ~~HTTP 400 en lugar de 403~~ | ✅ Resuelto 2026-03-01 (MapError) |
| ~~F-1~~ | ~~User registration no implementado~~ | ✅ Resuelto |
| F-2 | Outbox inoperativo | 🔴 Crítico |
| ~~B-1~~ | ~~SaveChanges doble no atómica~~ | ✅ Resuelto 2026-03-01 |
| ~~R-3~~ | ~~JWT SecretKey en repositorio~~ | ✅ Resuelto 2026-03-01 |
| ~~R-4~~ | ~~Reuse detection incompleta~~ | ✅ Resuelto 2026-03-01 |
| B-3 | Payment sin validar Trip | 🟠 Alto |
| ~~C-1~~ | ~~DbUpdateConcurrencyException no manejada~~ | ✅ Resuelto 2026-03-01 |
| F-3 | Ratings sin Application/API | 🟠 Alto |
| F-4 | Pagos solo en Pending | 🟠 Alto |
| F-5 | SignalR no implementado | 🟠 Alto |
| T-1 | Cobertura de tests parcial | 🟠 Alto |
| B-4 | StatusHistory entry inicial incorrecto | 🟡 Medio |
| B-5 | LicenseNumber no persistido | 🟡 Medio |
| F-6 | Admin sin gestión de usuarios | 🟡 Medio |
| A-1 | Doble archivo de solución | 🟡 Medio |
| A-2 | Rate limiter no distribuido | 🟡 Medio |
| A-3 | OTLP export no configurado | 🟡 Medio |
| A-4 | JwtSettings en archivo incorrecto | 🟢 Bajo |
| A-5 | appsettings.Development apunta a Docker | 🟢 Bajo |
