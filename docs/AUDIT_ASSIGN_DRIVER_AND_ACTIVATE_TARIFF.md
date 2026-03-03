# Auditoría: Assign Driver (409) y Activate Tariff (400)

**Tech Lead + QA Senior .NET (Movix).** Sin ejecutar app; análisis sobre código y reporte E2E.

---

# PARTE 1 — POST /api/v1/trips/{id}/assign-driver (409)

## 1) Handler y origen del 409

| Qué | Dónde |
|-----|--------|
| **Endpoint** | `POST /api/v1/trips/{id}/assign-driver` |
| **Controller** | `Movix.Api/Controllers/V1/TripsController.cs` → `AssignDriver(Guid id, ct)` (líneas 59–66). |
| **Handler** | `Movix.Application/Trips/Commands/AssignDriver/AssignDriverCommandHandler.cs` → método **`Handle`**. |

Origen del **409**:

- En el handler, líneas **105–109**:
  - `try { await _uow.SaveChangesAsync(ct); }`
  - `catch (ConcurrencyException) { return Result<TripDto>.Failure("No drivers available", "NO_DRIVERS_AVAILABLE"); }`
- En el controller, **MapError** (línea 130):
  - `"NO_DRIVERS_AVAILABLE" => Conflict(...)` → **409**.

Por tanto el 409 sale de **ConcurrencyException** en `SaveChangesAsync` (UnitOfWork re-lanza `DbUpdateConcurrencyException` como `ConcurrencyException`), pero se **mapea incorrectamente** a mensaje/código de “no hay conductores”, cuando en realidad **sí hubo driver seleccionado** y falló por **concurrencia**.

---

## 2) Happy path esperado

Flujo en código:

1. **Selección de driver disponible**
   - `_availabilityRepository.GetFirstAvailableAsync(ct)` (línea 61).
   - En **DriverAvailabilityRepository**: `IsOnline == true`, `CurrentTripId == null`, filtro por tenant (`Driver.TenantId == _tenantContext.TenantId`), orden por `UpdatedAtUtc`, `FirstOrDefaultAsync`.
2. **Si no hay disponibilidad**
   - `availability == null` → `Result.Failure("No drivers available", "NO_DRIVERS_AVAILABLE")` (líneas 62–63).
3. **Actualización de trip**
   - `trip.DriverId = availability.DriverId`, `trip.Status = Accepted`, audit fields (líneas 71–75).
4. **Inserción de TripStatusHistory**
   - Se construye `TripStatusHistory` y se persiste con **`_tripRepository.AddStatusHistoryAsync(history, ct)`** (líneas 77–89).
5. **Actualización de availability**
   - `availability.CurrentTripId = trip.Id`, `availability.UpdatedAtUtc = now` (líneas 90–91).
6. **Outbox**
   - `_outboxRepository.AddAsync(...)` (líneas 92–100).
7. **Persistencia**
   - `_uow.SaveChangesAsync(ct)` (línea 103).

Conclusión: el “happy path” (consulta, actualización de availability, cambio de estado del trip, inserción de history, outbox, SaveChanges) está correctamente implementado.

---

## 3) Confirmación fix BUG-002 en Assign Driver

- **Sí está aplicado** en Assign Driver:
  - No se usa `trip.StatusHistory.Add(...)`.
  - Se usa **inserción explícita** vía **`_tripRepository.AddStatusHistoryAsync(history, cancellationToken)`** (línea 89), que en infraestructura hace `_db.TripStatusHistories.Add(history)`.
  - Un único `SaveChangesAsync` persiste trip, history, availability y outbox.

No se requiere cambio por BUG-002 en este handler.

---

## 4) Por qué sigue habiendo ConcurrencyException y mitigación

**Causa probable:**

- **DriverAvailability** tiene **RowVersion** (concurrency token) y **Trip** también.
- **GetFirstAvailableAsync** no bloquea filas: dos requests concurrentes pueden obtener **el mismo** driver disponible.
- Ambos actualizan:
  - su **Trip** (distintos trips),
  - la **misma** fila de **DriverAvailability** (`CurrentTripId`, `UpdatedAtUtc`, `RowVersion`).
- El primer `SaveChangesAsync` actualiza esa fila; el segundo, al intentar actualizar la misma fila con el mismo RowVersion previo, obtiene 0 filas actualizadas → **DbUpdateConcurrencyException** → **ConcurrencyException** en el handler.

**Mitigación robusta (Clean Architecture):**

- **A) Transacción + SELECT … FOR UPDATE SKIP LOCKED (Postgres)**  
  - Añadir en capa de persistencia un método que, dentro de la misma transacción que luego hace los updates, seleccione una fila disponible con `FOR UPDATE SKIP LOCKED` (p. ej. `GetFirstAvailableAndLockAsync` o reserva atómica), de modo que solo una petición “se lleve” a ese driver.  
  - La interfaz del repositorio vive en Application; la implementación con SQL/EF en Infrastructure.

- **B) Reintento limitado**  
  - En el handler, ante `ConcurrencyException`, reintentar (p. ej. 1–2 veces) volviendo a llamar a `GetFirstAvailableAsync` (otro driver) y rehacer la asignación.  
  - Mantiene la arquitectura actual sin raw SQL; reduce la probabilidad de 409 pero no elimina la carrera en escenarios muy concurrentes.

- **C) Constraint / índice**  
  - Ya existe índice `IX_driver_availability_IsOnline_CurrentTripId` (búsqueda de disponibles).  
  - Un **unique partial index** en Postgres que impida dos filas con `CurrentTripId` no nulo para el mismo `DriverId` no aplica (una fila por driver).  
  - No hay constraint que por sí solo evite la doble asignación del mismo driver; la solución es **bloqueo (A)** o **reintento (B)**.

Recomendación: **A** para eliminar la condición de carrera; **B** como complemento o solución mínima sin tocar SQL.

---

## 5) Ajuste del error mapping

- **Problema:** Ante `ConcurrencyException`, el handler devuelve `"No drivers available"` / `NO_DRIVERS_AVAILABLE`, lo que es falso cuando el fallo es por concurrencia.
- **Cambio propuesto:** En el `catch (ConcurrencyException)` devolver el mismo contrato que en TransitionTrip/AcceptTrip:
  - **Mensaje:** `"Concurrent modification"`.
  - **Código:** `"CONFLICT"` (ya mapeado en `MapError` a **409**).
- **Impacto:** El cliente que hoy interpreta `NO_DRIVERS_AVAILABLE` como “no hay conductores” pasará a recibir `CONFLICT` en este caso, pudiendo distinguir “reintentar asignación” de “no hay conductores”. Si existiera contrato acordado que exija mantener `NO_DRIVERS_AVAILABLE` para concurrency, habría que documentar el cambio y/o usar un código adicional (p. ej. `CONCURRENCY_CONFLICT`) manteniendo 409.

---

## 6) Entregables — Assign Driver

### Lista de archivos a tocar

| Archivo | Cambio |
|---------|--------|
| `src/Movix.Application/Trips/Commands/AssignDriver/AssignDriverCommandHandler.cs` | (1) catch → CONFLICT; (2) opcional: retry en ConcurrencyException. |
| `src/Movix.Application/Drivers/IDriverAvailabilityRepository.cs` | Opcional: `TryReserveFirstAvailableAsync` o similar para reserva atómica. |
| `src/Movix.Infrastructure/Persistence/Repositories/DriverAvailabilityRepository.cs` | Opcional: implementación con FOR UPDATE SKIP LOCKED o UPDATE ... WHERE CurrentTripId IS NULL RETURNING. |
| `src/Movix.Api/Controllers/V1/TripsController.cs` | Opcional: si se añade `CONCURRENCY_CONFLICT`, mapear a `Conflict()`. |
| `tests/Movix.Application.Tests/Trips/AssignDriver_*.cs` | Nuevo: tests unitarios (éxito + concurrencia). |

### Cambio aplicado (mapeo de error)

**AssignDriverCommandHandler.cs** — ya aplicado en código:

```diff
         try
         {
             await _uow.SaveChangesAsync(cancellationToken);
         }
         catch (ConcurrencyException)
         {
-            return Result<TripDto>.Failure("No drivers available", "NO_DRIVERS_AVAILABLE");
+            return Result<TripDto>.Failure("Concurrent modification", "CONFLICT");
         }
```

**Tests añadidos:** `tests/Movix.Application.Tests/Trips/AssignDriver_Tests.cs`
- `Assign_driver_success_changes_trip_to_Accepted_and_calls_AddStatusHistoryAsync`
- `When_SaveChanges_throws_ConcurrencyException_returns_CONFLICT_not_NO_DRIVERS_AVAILABLE`

Opcional (retry, 1 reintento) — no aplicado:

```diff
         var now = _dateTime.UtcNow;
         var userId = _currentUser.UserId?.ToString();

-        var fromStatus = trip.Status;
-        trip.DriverId = availability.DriverId;
+        const int maxAttempts = 2;
+        for (int attempt = 1; attempt <= maxAttempts; attempt++)
+        {
+            availability = attempt > 1
+                ? await _availabilityRepository.GetFirstAvailableAsync(cancellationToken)
+                : availability;
+            if (availability == null)
+                return Result<TripDto>.Failure("No drivers available", "NO_DRIVERS_AVAILABLE");
+
+            var fromStatus = trip.Status;
+            trip.DriverId = availability.DriverId;
         trip.Status = TripStatus.Accepted;
         trip.UpdatedAtUtc = now;
         trip.UpdatedBy = userId;

         var history = new TripStatusHistory
         {
             Id = Guid.NewGuid(),
             TripId = trip.Id,
             FromStatus = fromStatus,
             ToStatus = TripStatus.Accepted,
             CreatedAtUtc = now,
             UpdatedAtUtc = now,
             CreatedBy = userId,
             UpdatedBy = userId
         };
         await _tripRepository.AddStatusHistoryAsync(history, cancellationToken);

         availability.CurrentTripId = trip.Id;
         availability.UpdatedAtUtc = now;

         var payload = "{\"tripId\":\"" + trip.Id + "\",\"driverId\":\"" + availability.DriverId + "\",\"occurredAtUtc\":\"" + now.ToString("O") + "\"}";
         await _outboxRepository.AddAsync(new OutboxMessage
         {
             Id = Guid.NewGuid(),
             Type = "DriverAssigned",
             Payload = payload,
             CreatedAtUtc = now
         }, cancellationToken);

         try
         {
             await _uow.SaveChangesAsync(cancellationToken);
+                break;
         }
         catch (ConcurrencyException)
         {
+                if (attempt == maxAttempts)
             return Result<TripDto>.Failure("Concurrent modification", "CONFLICT");
+            }
         }
```

(En un retry real habría que re-leer el trip y no reutilizar el mismo `trip`/`history` en el segundo intento para no duplicar history; el fragmento anterior es ilustrativo; la opción más limpia es solo cambiar el catch a CONFLICT y, si se quiere menos 409, añadir reserva atómica en repositorio.)

### Tests unitarios mínimos

- **Assign driver success:** Dado un trip en Requested y un driver disponible (mock), al ejecutar `AssignDriverCommand` el resultado es éxito, el trip pasa a Accepted y se llama `AddStatusHistoryAsync` una vez con FromStatus=Requested, ToStatus=Accepted.
- **Concurrencia:** Simular dos asignaciones “simultáneas” al mismo trip (o dos trips con el mismo driver mockeado) de forma que la primera SaveChanges tenga éxito y la segunda lance ConcurrencyException; verificar que el segundo resultado sea fallo con **code `CONFLICT`** (no `NO_DRIVERS_AVAILABLE`).

---

# PARTE 2 — POST /api/v1/admin/tariffs/{id}/activate (400)

## 1) Endpoint / controller / handler

| Qué | Dónde |
|-----|--------|
| **Endpoint** | `POST /api/v1/admin/tariffs/{id}/activate` |
| **Controller** | `Movix.Api/Controllers/V1/TariffsController.cs` → método **`Activate(Guid id, ct)`** (líneas 54–65). |
| **Handler** | `Movix.Application/Pricing/Commands/ActivateTariffPlan/ActivateTariffPlanCommandHandler.cs` → método **`Handle`**. |

El `id` de la ruta es el **TariffPlanId**; el **TenantId** viene del contexto (header `X-Tenant-Id` + `RequireTenant`).

---

## 2) Validaciones que disparan 400 (y otros códigos)

Del handler y del controller:

| Orden | Condición | Código de error | HTTP en controller |
|-------|-----------|------------------|---------------------|
| 1 | `plan == null` | `TARIFF_NOT_FOUND` | **404** NotFound |
| 2 | `plan.TenantId != request.TenantId` | `TARIFF_CROSS_TENANT` | **403** Forbid |
| 3 | `HasOverlappingSamePriorityAsync(...) == true` | `TARIFF_PRIORITY_CONFLICT` | **400** BadRequest |
| Cualquier otro failure | — | — | **400** BadRequest |

Por tanto, el **400** en activate puede ser:

- **TARIFF_PRIORITY_CONFLICT** (explícito en controller).
- Cualquier otro `result.ErrorCode` no mapeado a 404/403 (default BadRequest).

En el código actual **no** hay validación “tarifa ya activa”; si `plan.IsActive` ya es `true`, se vuelve a asignar `plan.IsActive = true` y SaveChanges no cambia nada → **200** (idempotente de hecho).

---

## 3) Caso “ya activa”: 200 idempotente vs 400

- **Comportamiento actual:** No hay comprobación de “ya activa”. Se hace `plan.IsActive = true` y `SaveChangesAsync`. Si ya estaba activa, EF puede no marcar cambios y devolver 0 filas actualizadas, pero la respuesta al cliente es **200** con el DTO.
- **Recomendación:** Tratar “ya activa” como **idempotente y 200**: si `plan.IsActive` ya es `true`, devolver `Result.Success(Map(plan))` sin llamar a SaveChanges (opcional, para no tocar BD). Así se documenta y se prueba de forma estable.

---

## 4) Regla de conflicto de prioridad / ventana efectiva

- **Método:** `TariffPlanRepository.HasOverlappingSamePriorityAsync(tenantId, planId, priority, effectiveFromUtc, effectiveUntilUtc)`.
- **Regla:** Existe conflicto si **otra** tarifa del mismo tenant:
  - está **activa** (`IsActive == true`),
  - tiene el **mismo Priority**,
  - tiene **ventana efectiva que se solapa** con la del plan que se quiere activar.
- **Solapamiento:** Intervalos `[EffectiveFromUtc, EffectiveUntilUtc)` (Until exclusivo). La condición en código:
  - `(EffectiveUntilUtc == null || effectiveFromUtc == null || effectiveFromUtc < EffectiveUntilUtc)` y  
  - `(effectiveUntilUtc == null || EffectiveFromUtc == null || effectiveUntilUtc > EffectiveFromUtc)`.
- **Índice:** No hay partial index en el código actual que implemente esta regla; la lógica es en consulta EF (Where + AnyAsync).

**Mensajes/códigos para QA:**

- **TARIFF_PRIORITY_CONFLICT** (400): “Another active plan with the same priority has an overlapping effective window.”
- Si se desea más detalle en logs/QA: incluir `Priority` y, opcional, el id del plan en conflicto (solo en mensaje de error interno, no necesario en contrato si no está acordado).

---

## 5) Checklist para pruebas (Activate Tariff)

| # | Caso | Resultado esperado | Justificación |
|---|------|--------------------|----------------|
| 1 | Activar tarifa **inactiva** (sin solapamiento de misma prioridad) | **200** OK, body con `isActive: true` | Flujo normal. |
| 2 | Activar tarifa **ya activa** | **200** OK idempotente (mismo body) | Idempotencia; no error. |
| 3 | Conflicto de prioridad (otra activa, misma prioridad, ventana solapada) | **400** BadRequest, `code: "TARIFF_PRIORITY_CONFLICT"` | Validación de negocio: solo una activa por prioridad en la misma ventana. |
| 4 | Tarifa no encontrada | **404**, `TARIFF_NOT_FOUND` | Recurso inexistente. |
| 5 | Tarifa de otro tenant | **403**, `TARIFF_CROSS_TENANT` | Seguridad multi-tenant. |

**Sobre 409 vs 400 para conflicto de prioridad:**  
Es una **validación de reglas de negocio** (una activa por prioridad en ventana), no un conflicto de versión de recurso. Por tanto **400** con `TARIFF_PRIORITY_CONFLICT` es coherente. **409** quedaría para “el recurso fue modificado por otro” (p. ej. optimistic concurrency); aquí no aplica.

---

**Resumen ejecutivo**

- **Assign Driver:** El 409 viene del catch de ConcurrencyException mapeado a NO_DRIVERS_AVAILABLE. BUG-002 está aplicado (AddStatusHistoryAsync). La concurrencia se explica por dos assigns tomando el mismo driver (RowVersion en DriverAvailability). Solución: mapear ConcurrencyException a CONFLICT (409) con mensaje “Concurrent modification”; opcionalmente retry o reserva atómica (FOR UPDATE SKIP LOCKED / UPDATE … WHERE CurrentTripId IS NULL) para reducir/eliminar la carrera.
- **Activate Tariff:** El 400 en el run E2E es coherente con **TARIFF_PRIORITY_CONFLICT** (otra tarifa activa, misma prioridad, ventana solapada). “Ya activa” puede documentarse como 200 idempotente. Checklist de pruebas definido arriba.
