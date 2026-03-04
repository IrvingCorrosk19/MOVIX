# Tests – Movix API

Estructura de pruebas por endpoint (archivos `.http` para REST Client / IDE) y flujos E2E.

## Estructura

```
tests/
  common/
    variables.env    # BASE_URL, ADMIN_EMAIL, ADMIN_PASSWORD, TENANT_ID
    headers.md       # Documentación: Authorization, X-Tenant-Id, Idempotency-Key
  endpoints/
    auth/            # register, login, refresh, logout
    admin/           # tenants, trips, drivers, outbox, tariffs, ops
    drivers/         # onboarding, status, location
    trips/           # quote (fare), create, get, assign-driver, accept, arrive, start, complete, cancel
    payments/        # create, webhook, simulate-webhook
  flows/             # Flujos E2E (secuencias de varios endpoints)
```

## Uso

1. Copiar o referenciar `common/variables.env`: definir `BASE_URL`, `ADMIN_EMAIL`, `ADMIN_PASSWORD`, `TENANT_ID`.
2. En el IDE (VS Code con REST Client): configurar variables de entorno para que `{{BASE_URL}}`, `{{ACCESS_TOKEN}}`, etc. se resuelvan. Ejecutar primero `auth/login.http` y usar el `accessToken` como `ACCESS_TOKEN` en el resto de peticiones.
3. Cada archivo `.http` corresponde a **un solo endpoint**: descripción, headers, ejemplo de request y response/errores esperados.

## Headers

Ver `common/headers.md` para: **Authorization**, **X-Tenant-Id**, **Idempotency-Key**.
