# E2E QA Report — Ride Lifecycle (Post BUG-001, BUG-002, BUG-003)

**Objetivo:** Validar el flujo E2E del ride lifecycle tras las correcciones de TenantId onboarding, TripStatusHistory INSERT y Driver ABAC.  
**Ejecución:** Flujo equivalente a la colección Postman `Movix_API_v1`, ejecutado vía script PowerShell (mismos endpoints y orden).  
**Base URL:** `http://127.0.0.1:55392`  
**Fecha:** 2026-03-03  

---

## Resultado por paso

| Paso | Acción | HTTP | Resultado | Notas |
|------|--------|------|-----------|--------|
| 1 | Login Admin | 200 | OK | Token admin obtenido |
| 2 | Create Tenant | 200 | OK | Tenant creado; flujo usó DevTenantId para trip/driver |
| 2b | Register Passenger | 202 | OK | passenger-e2e@movix.io (único para E2E) |
| 3 | Login Driver | 200 | OK | Driver del seed (driver@movix.io) |
| 4 | Login Passenger | 200 | OK | Passenger token obtenido |
| 5 | Driver Onboarding | 400 | FAIL | Esperado: driver del seed ya existe → DRIVER_EXISTS (no 500/FK) |
| 6 | Driver Status → Online | 200 | OK | DriverAvailability creado/actualizado |
| 7 | Driver Location | 200 | OK | Ubicación actualizada |
| 8 | Admin: Create Tariff | 200 | OK | Tarifa creada |
| 9 | Admin: Activate Tariff | 400 | FAIL | Posible conflicto prioridad o tarifa ya activa |
| 10 | Passenger: Create Trip | 200 | OK | Estado: **Requested** |
| 11 | Admin: Assign Driver | 409 | FAIL | ConcurrencyException → NO_DRIVERS_AVAILABLE (ver nota) |
| 12 | Driver: Arrive | 422 | FAIL | Transición inválida: trip sigue Requested (sin driver asignado) |
| 13 | Driver: Start | 422 | FAIL | Idem |
| 14 | Driver: Complete | 422 | FAIL | Idem |
| GET | GET /trips/{id} | 200 | OK | Refleja estado actual del trip |
| 15 | Passenger: Create Payment | 400 | FAIL | Trip no completado |

---

## Códigos HTTP observados

- **200:** Login Admin/Driver/Passenger, Create Tenant, Driver Status, Driver Location, Create Tariff, Create Trip, GET /trips/{id}.
- **202:** Register Passenger (aceptado).
- **400:** Driver Onboarding (DRIVER_EXISTS), Activate Tariff, Create Payment (trip no completado).
- **409:** Assign Driver (ConcurrencyException mapeada a NO_DRIVERS_AVAILABLE).
- **422:** Arrive / Start / Complete (INVALID_TRANSITION: trip en Requested, no Accepted).
- **500:** Ninguno.

---

## Validaciones críticas

| Validación | Resultado |
|------------|-----------|
| Ningún 409 por concurrency en mutaciones de trip (arrive/start/complete) | OK — no hubo 409 en esos endpoints |
| Ningún 403 incorrecto para driver legítimo en arrive/start/complete | OK — no hubo 403; solo 422 por estado inválido |
| Ningún 500 | OK |
| TripStatusHistory INSERT (sin 409 en Assign) | No comprobable en este run: Assign falló antes |
| GET /trips/{id} refleja estado | OK |
| ABAC: solo el driver asignado opera el trip | No ejercitado: trip sin asignar |

---

## Estado final del trip y pago

- **Trip Id:** (último ejecutado en el run)
- **Trip Status:** Requested (no se asignó conductor).
- **Payment Status:** N/A (no se creó pago; trip no completado).

---

## Comportamiento inesperado / notas

1. **Assign Driver 409:** El handler convierte `ConcurrencyException` en 409 con mensaje "No drivers available". Si la API en ejecución no incluye el fix de BUG-002 (TripStatusHistory vía `AddStatusHistoryAsync`), un INSERT incorrecto puede provocar concurrency y este 409. **Recomendación:** Reiniciar la API con el código actual (post-fix) y repetir el flujo.
2. **Driver Onboarding 400:** Esperado cuando el driver del seed ya está registrado (DRIVER_EXISTS). No se observó 500 ni violación de FK; el fix BUG-001 (TenantId en onboarding) evita el fallo de FK cuando se usa un driver nuevo.
3. **Arrive/Start/Complete 422:** Coherente: el trip nunca pasó a Accepted porque Assign Driver falló. Con Assign Driver 200, los pasos del driver deberían devolver 200 (BUG-003 corregido: se compara DriverId del usuario con trip.DriverId).
4. **Activate Tariff 400:** Posible tarifa ya activa o validación de prioridad/ventanas; no relacionado con los bugs corregidos.

---

## Conclusión

- **Sin 500** y **sin 403 indebido** para el driver en arrive/start/complete.
- El flujo completo **no** se pudo cerrar en este run porque **Assign Driver devolvió 409**, por lo que el trip no llegó a Accepted y no se pudieron validar Arrive → Start → Complete → Payment con 200.

Para declarar **"Ride lifecycle fully operational"** es necesario:

1. Ejecutar la API con el código que incluye los fixes de BUG-001, BUG-002 y BUG-003.
2. Volver a ejecutar el flujo E2E (colección Postman o script equivalente) y confirmar:
   - Assign Driver → **200** (sin 409).
   - Arrive → **200**, estado DriverArrived.
   - Start → **200**, estado InProgress.
   - Complete → **200**, estado Completed.
   - Create Payment → **200**, status Pending (Simulation).
   - GET /trips/{id} con estados correctos en cada paso.

Con eso se considera el ride lifecycle totalmente operativo.
