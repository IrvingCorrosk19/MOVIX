# Assign-Driver 409 — Opciones de fix (sin aplicar)

**Objetivo:** Documentar opciones concretas según lo que arroje el log forense (Fase A). No implementar aún; aplicar solo tras confirmar entidad fallida (Trip vs DriverAvailability).

---

## 1. Interpretación del log (Fase A)

Tras reproducir un 409 y revisar el log de `UnitOfWork`:

- Si en el log aparece **EntityName=Trip**: el UPDATE a `trips` afectó 0 filas (RowVersion u otra condición).
- Si aparece **EntityName=DriverAvailability**: el UPDATE a `driver_availability` afectó 0 filas.

Pueden fallar ambas en la misma excepción si dos escrituras compiten. Priorizar el fix según la entidad que más aparezca en tus corridas.

---

## 2. Si falla Trip

### Causa típica

- `trips.RowVersion` es `bytea` con default `gen_random_bytes(8)` solo en INSERT. En PostgreSQL no hay trigger que actualice el valor en UPDATE, por lo que EF envía el mismo valor que leyó; si otro proceso ya actualizó la fila, el UPDATE con `WHERE Id = @p AND RowVersion = @v` puede afectar 0 filas.

### Opción 2a — Actualizar RowVersion en UPDATE (PostgreSQL)

**Archivos a tocar:**

- Nueva migración EF (o script SQL aplicado a mano): añadir trigger en `trips` que en cada UPDATE asigne `"RowVersion" = gen_random_bytes(8)`.
- No hace falta cambiar configuración EF si el trigger mantiene la columna como bytea y EF sigue enviando el valor leído (el siguiente read verá el nuevo). Para que EF use el valor nuevo tras UPDATE sin round-trip, ver opción 2b.

**Pasos:**

1. Crear migración o script SQL, por ejemplo:
   ```sql
   CREATE OR REPLACE FUNCTION trips_rowversion_trigger()
   RETURNS TRIGGER AS $$
   BEGIN
     NEW."RowVersion" := gen_random_bytes(8);
     RETURN NEW;
   END; $$ LANGUAGE plpgsql;

   CREATE TRIGGER tr_trips_rowversion
   BEFORE UPDATE ON trips
   FOR EACH ROW EXECUTE FUNCTION trips_rowversion_trigger();
   ```
2. Aplicar en el entorno donde ocurre el 409.
3. Re-ejecutar E2E; el log forense debe dejar de mostrar Trip en concurrency conflict (o reducirse mucho).

### Opción 2b — Refresh después de SaveChanges (solo si no usas trigger)

Si no quieres trigger y quieres que el token se actualice en memoria tras un UPDATE exitoso:

- En el handler no suele refreshearse el Trip tras SaveChanges; la respuesta ya está armada. Para otros flujos que reutilicen la entidad, se podría llamar `entry.Reload()` o re-consultar. Para assign-driver no es estrictamente necesario si el único problema es el 0 rows en UPDATE; el trigger (2a) es más directo.

### Opción 2c — Comprobar que EF genera el WHERE correcto

- Archivo: `src/Movix.Infrastructure/Persistence/Configurations/TripConfiguration.cs`.
- Verificar que `RowVersion` tiene `.IsConcurrencyToken()` (ya está).
- No quitar el token; sin él no hay condición en el UPDATE y se pierde la detección de concurrencia.

---

## 3. Si falla DriverAvailability

### Causa típica

- Igual que Trip: `driver_availability."RowVersion"` solo se rellena en INSERT con `gen_random_bytes(8)`; en UPDATE no se actualiza en BD, y dos assign que tocan el mismo driver pueden provocar 0 rows en el segundo UPDATE.

### Opción 3a — Trigger en driver_availability (recomendado)

**Archivos a tocar:**

- Nueva migración o script SQL.

**Pasos:**

1. Crear función y trigger análogos a Trip:
   ```sql
   CREATE OR REPLACE FUNCTION driver_availability_rowversion_trigger()
   RETURNS TRIGGER AS $$
   BEGIN
     NEW."RowVersion" := gen_random_bytes(8);
     RETURN NEW;
   END; $$ LANGUAGE plpgsql;

   CREATE TRIGGER tr_driver_availability_rowversion
   BEFORE UPDATE ON driver_availability
   FOR EACH ROW EXECUTE FUNCTION driver_availability_rowversion_trigger();
   ```
2. Aplicar en BD.
3. Re-ejecutar E2E y revisar log forense.

### Opción 3b — Configuración EF

- Archivo: `src/Movix.Infrastructure/Persistence/Configurations/DriverAvailabilityConfiguration.cs`.
- Mantener `IsConcurrencyToken()` en `RowVersion`; no cambiar comportamiento de escritura solo por config si el problema es que la BD no actualiza el bytea.

### Opción 3c — Refresh post-SaveChanges

- Igual que 2b: solo relevante si se reutiliza la entidad en el mismo request; para assign-driver no es el fix principal.

---

## 4. Hardening: reserva atómica del driver

Objetivo: que solo una petición pueda “quedarse” con un driver libre, sin depender solo del optimistic lock después de leer.

### Opción 4a — UPDATE ... WHERE CurrentTripId IS NULL RETURNING (raw SQL)

**Idea:** En lugar de (1) SELECT primer disponible y (2) UPDATE en SaveChanges, hacer un único UPDATE que reserve la fila y devuelva el DriverId.

**Archivos a tocar:**

- `src/Movix.Application/Drivers/IDriverAvailabilityRepository.cs`: añadir algo como `Task<DriverAvailability?> TryReserveFirstAvailableAsync(...)`.
- `src/Movix.Infrastructure/Persistence/Repositories/DriverAvailabilityRepository.cs`: implementar con `FromSqlRaw` o `ExecuteUpdate` (EF Core 7+) o ADO.
- `src/Movix.Application/Trips/Commands/AssignDriver/AssignDriverCommandHandler.cs`: llamar a “reserve” en lugar de GetFirstAvailableAsync + mutar + SaveChanges para availability; el trip y la historia siguen en la misma transacción.

**Pasos (resumen):**

1. En el repositorio, ejecutar SQL del estilo:
   ```sql
   UPDATE driver_availability
   SET "CurrentTripId" = @tripId, "UpdatedAtUtc" = @now, "RowVersion" = gen_random_bytes(8)
   WHERE "DriverId" = (
     SELECT "DriverId" FROM driver_availability
     WHERE "IsOnline" = true AND "CurrentTripId" IS NULL
       AND (tenant filter si aplica)
     ORDER BY "UpdatedAtUtc"
     LIMIT 1
     FOR UPDATE SKIP LOCKED
   )
   RETURNING "DriverId", "RowVersion", ...
   ```
   (La sintaxis exacta depende de si usas subconsulta con FOR UPDATE SKIP LOCKED o un UPDATE con FROM; el punto es que solo una transacción gana la fila.)
2. Si RETURNING devuelve fila, usar ese DriverId para el trip; si no, devolver “no driver available”.
3. Mantener el resto del flujo (trip, history, outbox) en la misma UnitOfWork para no cambiar arquitectura más de lo necesario.

### Opción 4b — SELECT FOR UPDATE SKIP LOCKED + UPDATE en la misma transacción

- En una transacción explícita: SELECT primer disponible con FOR UPDATE SKIP LOCKED, luego UPDATE CurrentTripId y RowVersion.
- Misma idea que 4a pero en dos pasos dentro de la misma transacción; el SKIP LOCKED evita que la segunda petición espere por la misma fila.

**Archivos a tocar:** Mismo repositorio e interfaz; posiblemente exponer transacción desde UnitOfWork si hoy no se usa explícitamente.

---

## 5. Corrección del mapeo de errores (contrato API)

**Problema:** Hoy, si el handler devuelve `CONFLICT` (por ConcurrencyException), el controller mapea a 409 pero el cuerpo puede no distinguir “no hay conductores” de “concurrencia”. Además, no debe traducirse ConcurrencyException a código `NO_DRIVERS_AVAILABLE`.

**Regla deseada:**

- `availability == null` → código **NO_DRIVERS_AVAILABLE** (409 o 404 según contrato actual; mantener solo cuando realmente no hay driver).
- `ConcurrencyException` en SaveChanges → código **CONCURRENCY_CONFLICT** (409), mensaje tipo “Concurrent modification; retry”.

### Pasos exactos (Paso D — ya aplicado en Entregable 4)

1. **AssignDriverCommandHandler.cs**
   - Donde hoy se hace `catch (ConcurrencyException) { return Result.Failure("Concurrent modification", "CONFLICT"); }`:
     - Cambiar a devolver código **CONCURRENCY_CONFLICT** y mensaje **"Concurrent modification; retry"** (por ejemplo `Result.Failure("Concurrent modification; retry", "CONCURRENCY_CONFLICT")`).
   - No cambiar la rama `availability == null`; debe seguir devolviendo **NO_DRIVERS_AVAILABLE**.

2. **TripsController.MapError**
   - Añadir caso: `"CONCURRENCY_CONFLICT" => Conflict(new { error = result.Error, code = result.ErrorCode })`.
   - Mantener `"NO_DRIVERS_AVAILABLE" => Conflict(...)` y `"CONFLICT" => Conflict(...)` (por si algún otro handler sigue usando "CONFLICT"); opcionalmente documentar que assign-driver usará CONCURRENCY_CONFLICT.

**Archivos a tocar:**

- `src/Movix.Application/Trips/Commands/AssignDriver/AssignDriverCommandHandler.cs`
- `src/Movix.Api/Controllers/V1/TripsController.cs`

Con esto el cliente puede distinguir:

- 409 + `code: "NO_DRIVERS_AVAILABLE"` → no hay conductor; puede reintentar más tarde o en otra zona.
- 409 + `code: "CONCURRENCY_CONFLICT"` → conflicto de concurrencia; reintentar la misma petición.

---

## 6. Orden recomendado de aplicación

1. **Siempre:** Aplicar corrección de contrato (sección 5 / Paso D) — cambio pequeño y aislado.
2. **Según log (Fase A):** Si suele fallar Trip → aplicar opción 2a (trigger Trip). Si suele fallar DriverAvailability → aplicar opción 3a (trigger DriverAvailability). Si fallan ambos, aplicar ambos triggers.
3. **Opcional (más carga):** Si tras triggers siguen 409 por contención en el mismo driver, valorar reserva atómica (4a o 4b).

No aplicar en este documento ningún cambio de BD ni de código; este doc solo deja listas las opciones y los archivos/pasos para que tú ejecutes cuando confirmes la causa con el log.
