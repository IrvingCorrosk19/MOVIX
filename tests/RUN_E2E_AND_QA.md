# Cómo ejecutar tests y E2E — Movix API

Instrucciones para validar estabilidad de endpoints sin ejecutar nada en tu nombre; tú ejecutas los comandos.

---

## 1. Tests unitarios / de aplicación

```powershell
cd c:\Proyectos\RiderFlow
dotnet test tests\Movix.Application.Tests\Movix.Application.Tests.csproj
```

Cubre, entre otros:

- BUG-004: al completar/cancelar un trip, `DriverAvailability.CurrentTripId` queda en null.
- BUG-004: al poner conductor Online en registro existente, `CurrentTripId` se limpia.

---

## 2. Tests de API (middleware)

```powershell
dotnet test tests\Movix.Api.Tests\Movix.Api.Tests.csproj
```

Cubre:

- BUG-005: request con `X-Tenant-Id` distinto al claim JWT → 403 y `code: "TENANT_MISMATCH"`.

---

## 3. Toda la suite de tests

```powershell
dotnet test
```

---

## 4. E2E (API en ejecución)

Requisitos: API corriendo (por ejemplo con `dotnet run` en `src/Movix.Api`) y dependencias (PostgreSQL, Redis, etc.) levantadas (p. ej. docker-compose).

### Script PowerShell (ciclo de vida)

```powershell
cd c:\Proyectos\RiderFlow\tests
.\qa_correct_lifecycle.ps1
```

Flujo: login admin/driver/passenger → driver online → crear tarifa → crear trip → assign-driver → arrive → start → complete. Tras complete, el conductor debe quedar disponible (sin reset manual en DB).

### Script multi-tenant

```powershell
.\qa_multitenant.ps1
```

Comprueba que un request con tenant incorrecto en header (vs JWT) reciba 403 TENANT_MISMATCH.

### Postman

Si usas la colección en `docs/Movix_API_v1.postman_collection.json`, configurar variables de entorno (base URL, tokens, tenant) y ejecutar la colección o los requests de lifecycle y multi-tenant.

---

## 5. Criterios PASS/FAIL (endpoints estables)

| Criterio | PASS | FAIL |
|----------|------|------|
| Tests | `dotnet test` todo verde | Algún proyecto en rojo |
| Lifecycle E2E | assign-driver → … → complete sin 409 por NO_DRIVERS_AVAILABLE; conductor disponible después | 409 en assign-driver o conductor sigue ocupado |
| Tenant | Request con X-Tenant-Id ≠ tenant del JWT → 403 + `TENANT_MISMATCH` | 200 o otro código sin TENANT_MISMATCH |

**Estable:** PASS en todos los criterios anteriores.

---

## 6. Reporte v2

Tras ejecutar y validar, rellenar evidencia en:

- `docs/QA_RELEASE_READINESS_v2.md`

Secciones: resumen ejecutivo, lista de endpoints validados, hallazgos (0 bloqueadores esperado), comandos exactos y criterios PASS/FAIL.
