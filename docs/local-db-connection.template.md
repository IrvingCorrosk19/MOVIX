# Conexión local PostgreSQL (plantilla)

Para desarrollo local con PostgreSQL instalado en Windows:

- **Binarios:** `C:\Program Files\PostgreSQL\18\bin`
- **Usuario:** postgres
- **Contraseña:** (definida en instalación; no commitear)
- **Base de datos:** movix (crear con `createdb -U postgres movix` y extensión PostGIS: `psql -U postgres -d movix -c "CREATE EXTENSION IF NOT EXISTS postgis;"`)

Cadena de conexión (Npgsql):

```
Host=localhost;Port=5432;Database=movix;Username=postgres;Password=TU_PASSWORD;Include Error Detail=true
```

La API usa `appsettings.Development.local.json` (gitignored) para sobreescribir la conexión en desarrollo. Copiar desde esta plantilla y rellenar la contraseña.
