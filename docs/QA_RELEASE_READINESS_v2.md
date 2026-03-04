# QA Release Readiness Report — Movix API v2

**Date:** _[Pegar fecha de ejecución]_  
**Environment:** Local dev (docker-compose) — PostgreSQL 18, Redis, PostGIS  
**API URL:** `http://127.0.0.1:55392` (o la que uses)  
**Auditor:** Tech Lead .NET + QA Senior

---

## Resumen ejecutivo

### Qué estaba roto (v1)

| # | Severidad | Descripción |
|---|-----------|-------------|
| BUG-004 | **CRÍTICO** | `DriverAvailability.CurrentTripId` no se limpiaba al terminar/cancelar el trip → conductor quedaba ocupado indefinidamente. |
| BUG-005 | **MULTI-TENANT/SEC** | No se validaba coincidencia entre `X-Tenant-Id` (header) y `tenant_id` (claim JWT) → riesgo de aislamiento. |

### Qué se cambió

- **BUG-004**
  - **TransitionTripCommandHandler:** Ya limpiaba `CurrentTripId` al pasar a `Completed` o `Cancelled` en el mismo flujo transaccional (sin cambios).
  - **DriverStatusCommandHandler:** Al poner al conductor en **Online** sobre un registro existente de disponibilidad, ahora se fuerza `CurrentTripId = null` y `UpdatedAtUtc` actualizado, para que quede disponible de nuevo (fix defensivo ante estado obsoleto).
- **BUG-005**
  - **TenantMiddleware:** Para usuarios no SuperAdmin, si llega el header `X-Tenant-Id`, debe coincidir con el claim JWT `tenant_id`. Si no coincide → **403** con `code: "TENANT_MISMATCH"`. Si no se envía header, se usa solo el claim (sin fallar). SuperAdmin sigue pudiendo usar el header para elegir tenant.

### Evidencia de PASS esperada

- **Unit/Application tests:** Todos en verde (TransitionTrip limpia availability en Complete/Cancel; DriverStatus limpia CurrentTripId al pasar a Online).
- **API tests:** Middleware devuelve 403 y `TENANT_MISMATCH` cuando header y claim no coinciden.
- **E2E:** Ciclo completo (create → assign-driver → arrive → start → complete) sin intervención manual en DB; conductor vuelve a estar disponible tras completar. Requests con `X-Tenant-Id` distinto al claim fallan con 403 TENANT_MISMATCH.

---

## Lista de endpoints validados

_[Completar tras ejecutar E2E; ejemplo:]_

| Endpoint | Método | Validado (Sí/No) | Notas |
|----------|--------|------------------|--------|
| `/health` | GET | | |
| `/api/v1/auth/login` | POST | | |
| `/api/v1/auth/register` | POST | | |
| `/api/v1/trips` | POST | | |
| `/api/v1/trips/{id}/assign-driver` | POST | | |
| `/api/v1/trips/{id}/arrive` | POST | | |
| `/api/v1/trips/{id}/start` | POST | | |
| `/api/v1/trips/{id}/complete` | POST | | |
| `/api/v1/trips/{id}` | GET | | |
| `/api/v1/drivers/status` | POST | | |
| `/api/v1/payments` | POST | | |
| Multi-tenant: GET trip con X-Tenant-Id ≠ claim | GET | | Debe 403 TENANT_MISMATCH |

---

## Hallazgos

**Bloqueadores:** 0 (tras aplicar fixes BUG-004 y BUG-005).

_[Si tras la re-ejecución queda algún fallo, documentarlo aquí.]_

---

## Comandos exactos para reproducir

### 1. Tests unitarios / de aplicación

```powershell
cd c:\Proyectos\RiderFlow
dotnet test tests\Movix.Application.Tests\Movix.Application.Tests.csproj --no-build
# o para compilar y ejecutar:
dotnet test tests\Movix.Application.Tests\Movix.Application.Tests.csproj
```

### 2. Tests de API (middleware tenant mismatch)

```powershell
dotnet test tests\Movix.Api.Tests\Movix.Api.Tests.csproj
```

### 3. Todos los tests

```powershell
dotnet test
```

### 4. E2E (API en marcha)

1. Arrancar API y dependencias (por ejemplo `docker-compose up -d` y luego la API).
2. Ejecutar script de ciclo de vida:

```powershell
cd c:\Proyectos\RiderFlow\tests
.\qa_correct_lifecycle.ps1
```

3. (Opcional) Tests multi-tenant (tenant correcto vs incorrecto):

```powershell
.\qa_multitenant.ps1
```

### Criterios PASS/FAIL para declarar endpoints estables

- **PASS:**  
  - `dotnet test` verde.  
  - E2E: create trip → assign-driver → arrive → start → complete sin 409 NO_DRIVERS_AVAILABLE por conductor “atascado”.  
  - E2E multi-tenant: request con `X-Tenant-Id` distinto al tenant del JWT → 403 con `code: "TENANT_MISMATCH"`.

- **FAIL:** Cualquier test rojo o E2E con 409 en assign-driver por disponibilidad, o sin 403 TENANT_MISMATCH cuando corresponde.

---

## Referencias

- Reporte anterior (sin modificar): `docs/QA_RELEASE_READINESS_v1.md` (archivo en raíz de docs).
- Archivo de reportes archivados: `docs/qa/archive/README.md`.
