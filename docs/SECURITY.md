# MOVIX — Seguridad

**Generado:** 2026-03-01
**Fuente:** Auditoría de código — commit `f9c77fc`

---

## 1. AUTENTICACIÓN

### JWT Bearer (HS256)

| Parámetro | Valor |
|---|---|
| Algoritmo | HMAC-SHA256 (HS256) |
| Librería | `Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.8 |
| Issuer | `movix` (configurable via `Jwt:Issuer`) |
| Audience | `movix` (configurable via `Jwt:Audience`) |
| Expiración access token | 15 minutos (configurable via `Jwt:AccessTokenExpirationMinutes`) |
| ClockSkew | `TimeSpan.Zero` — sin tolerancia de desfase de reloj |
| ValidateLifetime | Sí |
| ValidateIssuer | Sí |
| ValidateAudience | Sí |
| ValidateIssuerSigningKey | Sí |

**Claims en el access token:**

| Claim | Valor |
|---|---|
| `sub` | UserId (Guid) |
| `email` (ClaimTypes.Email) | Email del usuario |
| `role` (ClaimTypes.Role) | Rol (Passenger, Driver, Admin, Support) |
| `jti` | UUID único por token |

### Refresh Token

| Parámetro | Valor |
|---|---|
| Tipo | Opaco (64 bytes aleatorios via `RandomNumberGenerator.GetBytes(64)`) |
| Formato al cliente | Base64 string |
| Almacenamiento | SHA-256 hash en tabla `refresh_tokens` |
| Expiración | 7 días (configurable via `Jwt:RefreshTokenExpirationDays`) |
| Campo de revocación | `RevokedAtUtc` (nullable DateTime) |
| Traza de reemplazo | `ReplacedByTokenId` (nullable string) |
| IP de revocación | `RevokedByIp` (disponible en entidad, no se rellena actualmente) |

### Refresh Token Rotation

**Estado: Implementado.**

En cada uso del refresh token:
1. Se verifica que el token no esté expirado (`ExpiresAtUtc > UtcNow`)
2. Se verifica que no esté revocado (`RevokedAtUtc == null`)
3. Se genera un nuevo refresh token (nuevo registro en `refresh_tokens`)
4. El token anterior se revoca: `RevokedAtUtc = DateTime.UtcNow`
5. Se establece `ReplacedByTokenId` en el token anterior

### Reuse Detection

**Estado: Implementado (parcial).**

Si un token ya fue reemplazado (`ReplacedByTokenId != null`), se rechaza con `REFRESH_TOKEN_REUSE`.

**Limitación:** Solo se rechaza el token específico reutilizado. No se invalida la familia completa de tokens. Ver `TECHNICAL-DEBT.md` R-4.

---

## 2. AUTORIZACIÓN

### RBAC (Role-Based Access Control)

Roles definidos en `Movix.Domain.Enums.Role`:

| Rol | Int | Descripción |
|---|---|---|
| Passenger | 0 | Usuario pasajero (app móvil) |
| Driver | 1 | Conductor |
| Admin | 2 | Administrador del sistema |
| Support | 3 | Soporte al cliente |

**Mapa de acceso por endpoint:**

| Endpoint | Passenger | Driver | Admin | Support | Sin auth |
|---|---|---|---|---|---|
| POST /auth/login | ✓ | ✓ | ✓ | ✓ | ✓ |
| POST /auth/refresh | ✓ | ✓ | ✓ | ✓ | ✓ |
| POST /auth/logout | ✓ | ✓ | ✓ | ✓ | ✓ |
| POST /drivers/onboarding | — | ✓ | ✓ | — | — |
| POST /drivers/status | — | ✓ | ✓ | — | — |
| POST /drivers/location | — | ✓ | ✓ | — | — |
| POST /trips | ✓ | ✓ | ✓ | ✓ | — |
| GET /trips/{id} | ✓* | ✓* | ✓ | ✓ | — |
| POST /trips/{id}/accept | — | ✓ | ✓ | — | — |
| POST /trips/{id}/arrive | — | ✓ | ✓ | — | — |
| POST /trips/{id}/start | — | ✓ | ✓ | — | — |
| POST /trips/{id}/complete | — | ✓ | ✓ | — | — |
| POST /trips/{id}/cancel | ✓ | ✓ | ✓ | ✓ | — |
| POST /payments | ✓ | ✓ | ✓ | ✓ | — |
| GET /admin/trips | — | — | ✓ | ✓ | — |
| GET /admin/drivers | — | — | ✓ | ✓ | — |

_* Solo si es el pasajero o driver asignado al viaje (ABAC)_

### ABAC (Attribute-Based Access Control)

Implementado en la capa Application:

| Handler | Control aplicado | Estado |
|---|---|---|
| `GetTripQueryHandler` | `userId == passengerId OR driverId OR role in [Admin, Support]` | ✅ Correcto |
| `AcceptTripCommandHandler` | Driver extraído del JWT; vehículo debe pertenecer a ese driver | ✅ Correcto |
| `DriverOnboardingCommandHandler` | Opera sobre el driver del userId JWT | ✅ Correcto |
| `DriverStatusCommandHandler` | Opera sobre el driver del userId JWT | ✅ Correcto |
| `DriverLocationCommandHandler` | Opera sobre el driver del userId JWT | ✅ Correcto |
| `CreatePaymentCommandHandler` | PayerId = userId JWT | ✅ Parcial (no valida ownership del trip) |
| `TransitionTripCommandHandler` (arrive/start/complete) | Sin verificación de ownership | ❌ AUSENTE |
| `TransitionTripCommandHandler` (cancel) | Sin verificación de ownership | ❌ AUSENTE |

---

## 3. RATE LIMITING

**Framework:** ASP.NET Core built-in Rate Limiter (Fixed Window)

| Policy | Límite | Ventana | Endpoints cubiertos |
|---|---|---|---|
| `"auth"` | 10 req | 1 minuto | POST /auth/login, POST /auth/refresh |
| `"trips"` | 30 req | 1 minuto | POST /trips, POST /trips/{id}/accept, POST /drivers/status, POST /drivers/location |
| `"payments"` | 20 req | 1 minuto | POST /payments |

**Endpoints SIN rate limiting:**
- POST /auth/logout
- GET /trips/{id}
- POST /trips/{id}/arrive, /start, /complete, /cancel
- GET /admin/trips
- GET /admin/drivers

**Limitación crítica:** Implementación in-process. Los contadores no se comparten entre instancias. En despliegues con múltiples réplicas, el límite efectivo es `N × límite` donde N es el número de instancias.

---

## 4. PROTECCIÓN DE CONCURRENCIA

### Idempotencia (Redis)

| Endpoint | Idempotency-Key | TTL | Comportamiento |
|---|---|---|---|
| POST /trips | Requerido | 24 horas | Si key existe, retorna el trip previo sin re-insertar |
| POST /payments | Requerido | 24 horas | Si key existe, retorna el payment previo sin re-insertar |

**Implementación:** `RedisIdempotencyService` con prefijo `movix:idempotency:` en Redis.

### Optimistic Concurrency (RowVersion)

Entidades con `RowVersion`: User, Driver, Vehicle, Trip, Payment.

**Limitación:** Sin manejo de `DbUpdateConcurrencyException` en los handlers. Un conflicto real devuelve HTTP 500. Ver `TECHNICAL-DEBT.md` C-1.

---

## 5. SEGURIDAD DE DATOS

### Password Hashing

- Librería: `BCrypt.Net-Next` 4.0.3
- Algoritmo: BCrypt con factor de trabajo por defecto
- Verificación: `BCrypt.Verify(plaintext, hash)`

### Token Hashing

- Algoritmo: SHA-256 via `SHA256.HashData()`
- Aplicado a: refresh tokens antes de persistir en BD

### Sensitive Data en Logs

- `RequestLoggingMiddleware` NO loguea body de requests
- No se exponen credenciales, tokens ni PII en logs
- `Correlation ID` es el único dato que se inyecta al scope de logging

---

## 6. HEADERS DE SEGURIDAD HTTP

**Implementados** (vía `SecurityHeadersExtensions.UseSecurityHeaders()`):

| Header | Valor | Propósito |
|---|---|---|
| `X-Content-Type-Options` | `nosniff` | Previene MIME type sniffing |
| `X-Frame-Options` | `DENY` | Previene clickjacking |
| `X-XSS-Protection` | `1; mode=block` | Activar filtro XSS en browsers legacy |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Controla información de referrer |

**NO implementados:**

| Header | Prioridad | Descripción |
|---|---|---|
| `Content-Security-Policy` | 🟠 Alto | Define fuentes permitidas de contenido |
| `Strict-Transport-Security` | 🟠 Alto | Fuerza HTTPS (HSTS) |
| `Permissions-Policy` | 🟡 Medio | Restringe APIs del navegador |
| `Cross-Origin-Opener-Policy` | 🟡 Medio | Aísla el contexto de navegación |

---

## 7. CORRELACIÓN Y TRAZABILIDAD

### Correlation ID

- **Middleware:** `CorrelationIdMiddleware` (registrado antes de autenticación)
- **Header de entrada:** `X-Correlation-ID`
- **Comportamiento:** Si el header está presente, lo reutiliza; si no, genera un UUID nuevo
- **Propagación:** Se inyecta en `context.Items["CorrelationId"]` y se retorna en el header de respuesta
- **Logging:** `RequestLoggingMiddleware` lo agrega al scope de Serilog

---

## 8. CONFIGURACIÓN DE SEGURIDAD

### Variables de entorno requeridas en producción

| Variable | Descripción | Estado actual |
|---|---|---|
| `Jwt__SecretKey` | Clave HMAC para firmar JWT. **Obligatoria.** Mínimo 32 caracteres. No debe estar en el repositorio. Recomendado: variable de entorno, Docker secrets o gestor de secretos (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault). | Fail-fast al arranque si ausente, &lt; 32 chars o contiene "CHANGE_ME" |
| `ConnectionStrings__DefaultConnection` | Connection string de Postgres | ⚠️ Credenciales en docker-compose |
| `ADMIN_EMAIL` / `ADMIN_PASSWORD` | Credenciales del admin inicial | Solo en Development |

### Validaciones al arranque (JWT SecretKey)

- Se exige `Jwt:SecretKey` (o `Jwt__SecretKey` por variable de entorno) antes de configurar la autenticación.
- Validaciones: no null, no vacío, longitud ≥ 32, no puede contener la cadena "CHANGE_ME" (case-insensitive).
- Si no cumple: `InvalidOperationException` con mensaje: "JWT SecretKey must be provided via environment variable and be at least 32 characters."
- No hay fallback: el valor no debe venir de appsettings.json en repositorio.

---

## 9. GAPS DE SEGURIDAD IDENTIFICADOS

| ID | Severidad | Descripción | Referencia |
|---|---|---|---|
| R-1 | 🔴 Crítico | ABAC ausente en arrive/start/complete | TECHNICAL-DEBT.md R-1 |
| R-2 | 🔴 Crítico | Cancel sin control de acceso | TECHNICAL-DEBT.md R-2 |
| R-3 | 🟠 Alto | JWT SecretKey en repositorio | TECHNICAL-DEBT.md R-3 |
| R-4 | 🟠 Alto | Reuse detection no invalida familia completa | TECHNICAL-DEBT.md R-4 |
| — | 🟠 Alto | Headers CSP y HSTS ausentes | Sección 6 |
| — | 🟠 Alto | Rate limiter no distribuido (multi-instancia) | Sección 3 |
| — | 🟠 Alto | `RevokedByIp` disponible en entidad pero nunca se rellena | AuthService |
| — | 🟡 Medio | Logout sin [Authorize] — cualquiera puede llamarlo | TripsController |
| — | 🟡 Medio | Health checks públicos exponen estado interno | Program.cs |
| — | 🟡 Medio | Swagger habilitado sin restricción de entorno | Program.cs |

---

## 10. RECOMENDACIONES PRIORITARIAS

1. **[Crítico]** Implementar ABAC en `TransitionTripCommandHandler` para verificar ownership antes de cualquier transición.
2. **[Crítico]** Agregar verificación de ownership en el flujo de cancelación.
3. **[Alto]** Rotar y externalizar el JWT SecretKey a gestión de secretos (AWS Secrets Manager, Azure Key Vault, HashiCorp Vault).
4. **[Alto]** Implementar invalidación de familia completa de refresh tokens al detectar reuse.
5. **[Alto]** Agregar headers `Content-Security-Policy` y `Strict-Transport-Security`.
6. **[Alto]** Migrar rate limiter a backing store Redis para soporte multi-instancia.
7. **[Alto]** Manejar `DbUpdateConcurrencyException` en todos los handlers con estado mutable — responder HTTP 409.
8. **[Medio]** Restringir Swagger a entorno Development o protegerlo con autenticación en producción.
9. **[Medio]** Registrar `RevokedByIp` en el flujo de logout y reuse detection.
10. **[Medio]** Agregar `[Authorize]` al endpoint de logout para prevenir usos anónimos.
