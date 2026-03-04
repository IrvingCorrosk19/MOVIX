# Endpoint Stability Plan — Movix API

**Objetivo:** Cerrar “endpoints probados y estables” sin perder contexto y sin cambios masivos.  
**Reglas:** No borrar archivos existentes; no ejecutar API/pruebas desde este plan; micro-pasos revisables; no inventar resultados.

---

## 1. Single Source of Truth: qué significa “estables”

Se consideran **endpoints estables** cuando se cumple todo lo siguiente:

| Criterio | Definición operativa |
|----------|----------------------|
| **0 x 500** | Ninguna respuesta HTTP 500 en los flujos validados (errores de negocio → 4xx con código y mensaje; fallos inesperados no deben devolver 500 sin diagnóstico). |
| **Lifecycle completo** | Flujo create trip → assign-driver → arrive → start → complete (y opcional payment) termina con 200 en cada paso y estado final Completed, sin intervención manual en BD. |
| **Idempotency** | Reenvío con mismo Idempotency-Key devuelve mismo recurso (mismo id de trip/payment) y 200, sin duplicados. |
| **Multi-tenant** | X-Tenant-Id ≠ tenant_id del JWT → 403 con código TENANT_MISMATCH. Endpoints con RequireTenant respetan aislamiento. |
| **Concurrency esperado** | Dos assign-driver concurrentes sobre el mismo trip: uno 200, el otro 4xx (TRIP_INVALID_STATE o CONCURRENCY_CONFLICT). Dos assign sobre distintos trips no deben provocar 500; si compiten por el mismo driver, uno puede 409 CONCURRENCY_CONFLICT. |

**No estable** si: 500 sin log forense, lifecycle que exige reseteo manual de `CurrentTripId`, o confusión entre NO_DRIVERS_AVAILABLE y conflicto de concurrencia (mismo código/mensaje para ambos).

---

## 2. Matriz de riesgos

| Riesgo | Bloqueante (Sí/No) | Descripción |
|--------|--------------------|-------------|
| 409 en assign-driver sin distinguir “no hay driver” vs “concurrencia” | **Sí** | Cliente no puede decidir retry vs búsqueda en otra zona; soporte no puede diagnosticar. |
| 500 en assign-driver por DbUpdateConcurrencyException no manejada o mal traducida | **Sí** | Indica fallo de servidor; oculta causa real (concurrencia). |
| RowVersion/token de concurrencia no actualizado en BD en UPDATE | **Sí** | Puede provocar 409 repetidos o doble asignación bajo carga. |
| Falta de logging forense en UnitOfWork ante concurrency | **No** | Bloquea diagnóstico; no bloquea per se el flujo una vez corregido el mapeo de errores. |
| Lifecycle que requiere reseteo manual de CurrentTripId | **Sí** | Ya mitigado con fixes BUG-004; debe verificarse en regresión. |
| Tenant bypass (X-Tenant-Id ≠ claim) | **Sí** | Ya mitigado con BUG-005; debe verificarse en regresión. |

---

## 3. Plan por fases A / B / C / D

### Fase A — Diagnóstico determinístico del 409

| Objetivo | Logging forense en UnitOfWork al capturar DbUpdateConcurrencyException. |
|----------|-----------------------------------------------------------------------|
| Criterios de aceptación | (1) Al producirse DbUpdateConcurrencyException se registra: EntityName, PrimaryKey (Id), EntityState, valores original y actual del token de concurrencia (byte[] en HEX, uint como valor). (2) Helpers GetPrimaryKeyValue y GetConcurrencyTokenValues usados en el log. (3) Comportamiento funcional idéntico: solo log y rethrow ConcurrencyException con inner exception. (4) appsettings.Development.json eleva a Information solo para Microsoft.EntityFrameworkCore, Movix, Movix.Infrastructure.Persistence. |
| Entregables | UnitOfWork.cs con helpers y log detallado; appsettings.Development.json con Logging. |
| Evidencia | Tras ejecutar un flujo que provoque 409, el log debe contener entidad(es) y tokens para análisis. |

### Fase B — Opciones de fix (sin aplicar aún)

| Objetivo | Documentar opciones concretas de fix según lo que arroje el log (Trip vs DriverAvailability). |
|----------|----------------------------------------------------------------------------------------------|
| Criterios de aceptación | (1) Documento ASSIGN_DRIVER_409_FIX_OPTIONS.md creado. (2) Si falla Trip: opciones EF (insert/refresh token). (3) Si falla DriverAvailability: mismas opciones. (4) Opción de reserva atómica (UPDATE ... WHERE CurrentTripId IS NULL RETURNING / SELECT FOR UPDATE SKIP LOCKED). (5) Propuesta de código CONCURRENCY_CONFLICT y mantener NO_DRIVERS_AVAILABLE solo cuando availability == null. Todo con pasos y archivos a tocar; sin aplicar cambios de fix. |
| Entregables | docs/ASSIGN_DRIVER_409_FIX_OPTIONS.md. |

### Fase C — Aplicar fix de concurrencia (según Fase B)

| Objetivo | Aplicar el fix mínimo elegido a partir del diagnóstico (Fase A) y del doc de opciones (Fase B). |
|----------|------------------------------------------------------------------------------------------------|
| Criterios de aceptación | (1) Fix aplicado en los archivos indicados en Fase B. (2) Tests existentes en verde. (3) E2E lifecycle sin 500; 409 solo con código CONCURRENCY_CONFLICT cuando corresponda. |
| Entregables | Cambios en EF/config o repositorio según opción elegida; sin refactors globales. |
| Evidencia | Logs de una corrida E2E; resultado de dotnet test. |

### Fase D — Corrección de contrato de error (Assign Driver)

| Objetivo | Distinguir en API: (a) no hay driver → NO_DRIVERS_AVAILABLE, (b) concurrencia → CONCURRENCY_CONFLICT (409). |
|----------|-------------------------------------------------------------------------------------------------------------|
| Criterios de aceptación | (1) AssignDriverCommandHandler: availability == null → NO_DRIVERS_AVAILABLE; catch ConcurrencyException → CONCURRENCY_CONFLICT con mensaje “Concurrent modification; retry”. (2) TripsController.MapError: CONCURRENCY_CONFLICT → 409 Conflict. (3) No se cambia otro comportamiento del lifecycle. |
| Entregables | AssignDriverCommandHandler.cs (código de error y mensaje); TripsController.cs (MapError). |
| Evidencia | Llamada con driver disponible → 200; sin drivers → 409 con code NO_DRIVERS_AVAILABLE; concurrencia → 409 con code CONCURRENCY_CONFLICT. |

---

## 4. Checklist de ejecución manual (orden recomendado)

Ejecutar **tú** en este orden; no se asume que nada se ejecute desde este plan.

1. **Preparar entorno**
   - [ ] BD y Redis levantados (p. ej. docker-compose).
   - [ ] `dotnet build` en la solución (o proyectos necesarios) sin errores.

2. **Fase A (logging)**
   - [ ] Aplicar cambios de UnitOfWork y appsettings.Development.json.
   - [ ] Arrancar API en Development.
   - [ ] Reproducir un 409 (p. ej. dos assign-driver concurrentes al mismo trip, o mismo driver para dos trips).
   - [ ] Revisar logs: deben aparecer EntityName, PrimaryKey, EntityState, concurrency token (HEX/valor).

3. **Fase B (documento)**
   - [ ] Leer docs/ASSIGN_DRIVER_409_FIX_OPTIONS.md.
   - [ ] Decidir opción de fix según entidad que apareció en el log (Trip vs DriverAvailability).

4. **Fase C (fix de concurrencia, si aplica)**
   - [ ] Aplicar solo el fix elegido en Fase B.
   - [ ] `dotnet test` (proyectos de tests) y anotar resultado.
   - [ ] Ejecutar E2E lifecycle (script existente); anotar si hay 500 o 409 y con qué código.

5. **Fase D (contrato de error)**
   - [ ] Aplicar cambios de AssignDriverCommandHandler y MapError (CONCURRENCY_CONFLICT).
   - [ ] Verificar: sin drivers → 409 NO_DRIVERS_AVAILABLE; concurrencia → 409 CONCURRENCY_CONFLICT.
   - [ ] Regresión rápida: lifecycle completo + idempotency + multi-tenant.

6. **Cierre**
   - [ ] Revisar que no queden 500 en los flujos validados.
   - [ ] Adjuntar evidencia según sección 5.

---

## 5. Evidencia a adjuntar al final

| Evidencia | Descripción |
|-----------|-------------|
| **Logs (Fase A)** | Fragmento del log donde se ve DbUpdateConcurrencyException y el detalle forense (entidad, PK, token original/actual en HEX o valor). |
| **Salida de tests** | Resultado de `dotnet test` (o de los proyectos de test indicados en el plan). |
| **E2E lifecycle** | Salida del script E2E (PowerShell u otro) mostrando códigos HTTP por paso (200/4xx) y que no haya 500. |
| **Multi-tenant** | Salida de una llamada con X-Tenant-Id ≠ JWT mostrando 403 y TENANT_MISMATCH. |
| **Assign-driver: códigos** | Ejemplo de respuesta 409 con code NO_DRIVERS_AVAILABLE (sin drivers) y ejemplo con code CONCURRENCY_CONFLICT (concurrencia), si aplica. |

---

## 6. Runbook — assign-driver concurrency diagnostic

### Cómo ejecutar el script de reproducción

1. Arrancar la API en Development (p. ej. `dotnet run` en `src/Movix.Api`) con BD y Redis disponibles.
2. En otra terminal, desde la raíz del repo:
   ```powershell
   cd c:\Proyectos\RiderFlow
   .\scripts\reproduce_assign_driver_concurrency.ps1
   ```
3. El script hace login admin, deja un driver online, crea viaje(s) y lanza peticiones concurrentes a `POST /api/v1/trips/{id}/assign-driver`.

### Resultado esperado

- **Scenario A (mismo trip, dos assign):** Request 1 → 200, Request 2 → 400 con `TRIP_INVALID_STATE` (un viaje solo puede asignarse una vez).
- **Scenario B (dos trips, dos assign en paralelo):** se espera **200 + 409 CONCURRENCY_CONFLICT** cuando ambas peticiones compiten por el mismo driver; una gana y la otra recibe 409 con cuerpo `{ "error": "Concurrent modification; retry", "code": "CONCURRENCY_CONFLICT" }`.

### Dónde ver el log forense de UnitOfWork

- El log se escribe en la **consola donde corre la API** (stdout del proceso `dotnet run`), no en la salida del script de PowerShell.
- Al producirse `DbUpdateConcurrencyException`, `UnitOfWork` registra un mensaje de nivel **Warning** antes de relanzar `ConcurrencyException`.

### Línea exacta a capturar y pegar

Buscar en la consola de la API una línea que contenga:

```text
Concurrency conflict. Entity=... PrimaryKey=... State=... ConcurrencyToken=...
```

Ejemplo completo:

```text
Concurrency conflict. Entity=DriverAvailability PrimaryKey=DriverId=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx State=Modified ConcurrencyToken=RowVersion: Original=0x... Current=0x...
```

Capturar y adjuntar esa línea completa (con EntityName, PrimaryKey, State y ConcurrencyToken) como evidencia del diagnóstico.

---

## 7. Stability Gate — assign-driver

Criterio para considerar **estable** el endpoint `POST /api/v1/trips/{id}/assign-driver`:

| Gate | Condición | Resultado |
|------|-----------|-----------|
| **PASS** | Se puede reproducir 409 con código **CONCURRENCY_CONFLICT** (p. ej. Scenario B del script) y en la consola de la API se captura la línea forense con **EntityName**, **PrimaryKey** y **tokens** (Original/Current). Además, cuando no hay driver disponible se recibe 409 con **NO_DRIVERS_AVAILABLE**, y cuando hay driver disponible el flujo normal devuelve 200. | Endpoint estable para cierre de estabilidad. |
| **FAIL** | Con al menos un driver **Online** y disponible (`CurrentTripId` null), una llamada a assign-driver devuelve 409 con código **NO_DRIVERS_AVAILABLE**. Eso indica que se está confundiendo “no hay driver” con otro fallo (p. ej. concurrencia) o que la disponibilidad no se está evaluando bien. | No cerrar estabilidad; revisar lógica de disponibilidad y mapeo de errores. |

Resumen: **PASS** = 409 CONCURRENCY_CONFLICT reproducible con log forense capturado y NO_DRIVERS_AVAILABLE solo cuando realmente no hay conductor disponible. **FAIL** = NO_DRIVERS_AVAILABLE con conductor Online disponible.

---

Con esto se puede cerrar “endpoints probados y estables” de forma auditable y sin perder contexto.
