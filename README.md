# MOVIX — Backend API

Plataforma de movilidad urbana (taxi tipo Uber). Backend en .NET 8 con Clean Architecture, listo para producción y escalado horizontal.

## Requisitos

- .NET 8 SDK
- Docker y Docker Compose (recomendado)
- PostgreSQL 16+ con PostGIS (o uso de imagen `postgis/postgis:16-3.4-alpine` en Docker)
- Redis 7+

## Ejecución con Docker Compose

1. Clonar el repositorio y situarse en la raíz del proyecto.

2. Construir y levantar los servicios:

```bash
docker-compose up -d --build
```

3. La API quedará disponible en `http://localhost:8080`.

4. Aplicación de migraciones: se ejecutan automáticamente al arrancar la API (en `Program.cs`).

5. Swagger UI: `http://localhost:8080/swagger`.

6. Health checks:
   - `GET http://localhost:8080/health` — estado general.
   - `GET http://localhost:8080/ready` — listo para tráfico (PostgreSQL y Redis).

## Ejecución en local (sin Docker)

Requisitos: PostgreSQL con PostGIS y Redis en ejecución (p. ej. `localhost:5432` y `localhost:6379`). La API usa la base de datos **movix** en desarrollo cuando existe `appsettings.Development.local.json`.

**Guía detallada:** [docs/setup/Local-API-PostGIS.md](docs/setup/Local-API-PostGIS.md)

### Opción rápida — script PowerShell

Desde la raíz del repo:

```powershell
# Arrancar API (JWT y entorno Development se configuran en el script)
.\scripts\run-api-local.ps1

# Aplicar migraciones y luego arrancar
.\scripts\run-api-local.ps1 -Migrations
```

El script exige `Jwt__SecretKey` de al menos 32 caracteres (por defecto usa una clave de desarrollo). Para usar tu propia clave: `.\scripts\run-api-local.ps1 -JwtSecret "tu-clave-minimo-32-chars"`.

### Pasos manuales

1. Crear la base de datos y PostGIS:

   ```bash
   createdb -U postgres movix
   psql -U postgres -d movix -c "CREATE EXTENSION IF NOT EXISTS postgis;"
   ```

2. Configurar conexión: crear o editar `src/Movix.Api/appsettings.Development.local.json` con `ConnectionStrings:DefaultConnection` apuntando a `Database=movix` y `Jwt:SecretKey` (≥32 caracteres). Ver plantilla en [docs/local-db-connection.template.md](docs/local-db-connection.template.md).

3. Aplicar migraciones (entorno Development para usar la conexión local):

   ```powershell
   cd src/Movix.Api
   $env:ASPNETCORE_ENVIRONMENT = "Development"
   dotnet ef database update --project ../Movix.Infrastructure/Movix.Infrastructure.csproj --startup-project .
   ```

4. Ejecutar la API (JWT por variable de entorno):

   ```powershell
   $env:Jwt__SecretKey = "clave-de-desarrollo-local-minimo-32-caracteres"
   $env:ASPNETCORE_ENVIRONMENT = "Development"
   dotnet run --project src/Movix.Api/Movix.Api.csproj
   ```

La API escuchará en el puerto configurado (p. ej. según `launchSettings.json`). Health checks incluyen **postgis** (`SELECT PostGIS_Version()`); ver `/health` para comprobar que PostGIS está activo.

## Aplicar migraciones en Docker

Con el stack levantado (`docker-compose up -d`), las migraciones se aplican al iniciar la API. Para aplicarlas manualmente desde el host (BD en localhost:5432):

```bash
cd src/Movix.Api
dotnet ef database update --project ../Movix.Infrastructure/Movix.Infrastructure.csproj --context MovixDbContext
```

Con la API en Docker y Postgres en Docker, la API ya ejecuta `MigrateAsync()` al arrancar, por lo que AddSpatialIndexes y el resto se aplican solas.

## Crear migraciones EF Core

Desde la raíz del repositorio:

```bash
cd src/Movix.Api
dotnet ef migrations add NombreMigracion --project ../Movix.Infrastructure/Movix.Infrastructure.csproj --context MovixDbContext
```

## Variables de entorno (Docker / producción)

- `ConnectionStrings__DefaultConnection`: cadena de conexión a PostgreSQL.
- `ConnectionStrings__Redis`: cadena de conexión a Redis (ej. `redis:6379`).
- `Jwt__SecretKey`: clave secreta JWT (mínimo 32 caracteres).
- `Jwt__Issuer`, `Jwt__Audience`: emisor y audiencia del token (opcional).
- `Stripe__SecretKey`: clave secreta de API Stripe (Sandbox: `sk_test_...`).
- `Stripe__WebhookSecret`: secreto de firma del webhook Stripe (`whsec_...`).
- `ASPNETCORE_ENVIRONMENT`: `Development` o `Production`.
- `ASPNETCORE_URLS`: URLs de escucha (ej. `http://+:8080`).

## Seed (solo Development)

Con `ASPNETCORE_ENVIRONMENT=Development`, al arrancar la API se ejecuta un seed mínimo. No se guardan contraseñas en el repositorio; se usan variables de entorno.

**Requeridas para usuario Admin inicial:**

- `ADMIN_EMAIL`: email del usuario Admin.
- `ADMIN_PASSWORD`: contraseña del usuario Admin.

**Opcionales para driver + vehicle de ejemplo:**

- `DRIVER_EMAIL`: email del usuario conductor.
- `DRIVER_PASSWORD`: contraseña del usuario conductor.

Ejemplo (PowerShell):

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ADMIN_EMAIL = "admin@movix.local"
$env:ADMIN_PASSWORD = "TuPasswordSeguro"
# Opcional:
$env:DRIVER_EMAIL = "driver@movix.local"
$env:DRIVER_PASSWORD = "TuPasswordSeguro"
cd src/Movix.Api; dotnet run
```

Los roles (Passenger, Driver, Admin, Support) son valores del enum en dominio; no se insertan en BD. El seed solo crea usuarios (Admin y opcionalmente Driver) y, si aplica, un vehículo de ejemplo para el driver.

## Endpoints principales

- **Auth:** `POST /api/v1/auth/login`, `POST /api/v1/auth/refresh`, `POST /api/v1/auth/logout`
- **Driver:** `POST /api/v1/drivers/onboarding`, `POST /api/v1/drivers/status`, `POST /api/v1/drivers/location`
- **Trips:** `POST /api/v1/trips` (header `Idempotency-Key` obligatorio), `GET /api/v1/trips/{id}`, `POST /api/v1/trips/{id}/accept|arrive|start|complete|cancel`
- **Payments:** `POST /api/v1/payments` (header `Idempotency-Key` obligatorio; devuelve `clientSecret` para Stripe). Webhook: `POST /api/v1/payments/webhook` (header `Stripe-Signature`; sin auth). Ejemplo curl webhook (referencia): `curl -X POST http://localhost:8080/api/v1/payments/webhook -H "Stripe-Signature: <firma>" -H "Content-Type: application/json" -d @payload.json`
- **Admin:** `GET /api/v1/admin/trips`, `GET /api/v1/admin/drivers` (roles Admin/Support)

## Observabilidad

- Logs estructurados con Serilog (prefijo `movix`).
- Correlation ID en header `X-Correlation-ID` en cada petición.
- Health: `/health` y `/ready`.
- OpenTelemetry habilitado para tracing y métricas (configurable).

## Seguridad

- JWT access token (corto) y refresh token rotativo con detección de reutilización.
- RBAC: Passenger, Driver, Admin, Support.
- ABAC: control por propietario del recurso (ej. GET trip solo pasajero/conductor/admin).
- Rate limiting en login, trips y payments.
- Header `Idempotency-Key` obligatorio en creación de viajes y pagos.
- Security headers (X-Content-Type-Options, X-Frame-Options, etc.).
