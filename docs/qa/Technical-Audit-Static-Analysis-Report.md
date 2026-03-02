# Auditoría técnica — Análisis estático (sin ejecución E2E)

**Rol:** QA Engineer Senior — Sistemas transaccionales críticos  
**Alcance:** Revisión de implementación desde código fuente. No se modificó código. No se asumen resultados de ejecución.  
**Contexto:** Las pruebas E2E no pudieron ejecutarse por falta de JWT SecretKey (≥32 caracteres), base de datos no configurada/accesible e imposibilidad de generar tokens válidos.

---

## 1. Resumen ejecutivo

Se realizó una auditoría estática sobre la configuración crítica (JWT, Stripe), autenticación y autorización, máquina de estados de viajes, integridad financiera de pagos y concurrencia. Se identifican bloqueantes técnicos actuales para E2E, riesgos derivados del análisis de código, escenarios no validables sin entorno y requisitos concretos para habilitar pruebas reales. El sistema presenta validaciones fuertes en arranque (JWT, longitud de secreto, rechazo de "CHANGE_ME"), uso de RowVersion y ConcurrencyException en entidades críticas, y máquina de estados explícita y cerrada; por otro lado, se detectan riesgos de secreto en configuración, posible doble asignación de conductor, ausencia de validación Amount vs FinalAmount y de restricción "un pago por (TripId, PayerId)", e inconsistencia entre rol SuperAdmin en handlers y su ausencia en [Authorize(Roles)]. El nivel de confianza actual se considera **medio-bajo** para uso en producción sin ejecutar E2E y sin corregir los hallazgos de integridad financiera y concurrencia.

---

## 2. Bloqueantes técnicos actuales

| Bloqueante | Ubicación | Descripción |
|------------|-----------|-------------|
| JWT SecretKey obligatorio | Program.cs líneas 43-46 | Si `Jwt:SecretKey` está vacío, es menor de 32 caracteres o contiene "CHANGE_ME", se lanza `InvalidOperationException` y la aplicación no inicia. Sin este valor no se puede emitir ni validar tokens. |
| Stripe (modo no Simulation) | Program.cs líneas 47-54 | Si `Payments:Mode` no es "Simulation", se exigen `Stripe:SecretKey` y `Stripe:WebhookSecret`; si faltan, arranque falla. |
| Base de datos y migraciones | Program.cs líneas 154-160 | Tras el pipeline se ejecuta `db.Database.MigrateAsync()` y `DataSeeder.SeedAsync()`. Sin conexión a PostgreSQL el arranque falla. |
| Redis (health check) | Program.cs línea 93 | Health checks registran Redis; la API puede arrancar pero el check de readiness falla si Redis no está disponible. Idempotency puede depender de Redis. |
| Seed condicional | DataSeeder.cs líneas 28-29 | El seed solo corre en `environmentName == "Development"`. En otros entornos no se crean tenant ni usuarios de prueba. |
| Usuarios seed opcionales | DataSeeder.cs líneas 36-44 | Admin y Driver se crean solo si existen `ADMIN_EMAIL`, `ADMIN_PASSWORD`, `DRIVER_EMAIL`, `DRIVER_PASSWORD` en configuración. Sin ellos no hay usuarios para probar por rol. |

---

## 3. FASE 1 — Análisis de configuración crítica

### 3.1 Dónde se valida JWT SecretKey

- **Program.cs (líneas 43-46):** Se lee `builder.Configuration["Jwt:SecretKey"]` (cualquier origen de configuración: appsettings, variables de entorno, etc.). No se exige que sea exclusivamente variable de entorno.
- **Condiciones de rechazo:** `string.IsNullOrWhiteSpace(secret)` O `secret.Length < 32` O `secret.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase)` → se lanza `InvalidOperationException("JWT SecretKey must be provided via environment variable and be at least 32 characters.")`.

### 3.2 Validación de longitud mínima

- **Sí existe:** se exige `secret.Length >= 32`. No hay validación de longitud máxima ni de complejidad (mayúsculas, números, símbolos).

### 3.3 Comportamiento si la variable no existe

- Si la clave no está configurada o está vacía, la aplicación **no arranca**: se lanza la excepción antes de `app.Run()`. No hay fallback ni valor por defecto.

### 3.4 Riesgos si se configura incorrectamente

| Riesgo | Severidad | Descripción |
|--------|-----------|-------------|
| Secreto en appsettings | Alto | El mensaje de error menciona "environment variable", pero el código usa `Configuration["Jwt:SecretKey"]`, por lo que el valor puede venir de appsettings.json o appsettings.Development.local.json. Si el secreto se versiona o se comparte, queda expuesto. |
| "CHANGE_ME" rechazado | Bajo | Evita dejar el valor de ejemplo; no evita valores débiles (ej. 32 espacios o cadenas predecibles). |
| Sin rotación en caliente | Medio | El secreto se lee una vez al inicio. Cambiar la variable de entorno requiere reinicio para que tenga efecto. |
| AuthService redundante | Bajo | AuthService (línea 169) usa `_jwtSettings.SecretKey` y lanzaría si fuera null al generar un token; en la práctica Program ya ha impedido el arranque con secreto inválido. |
| Stripe en Development | Medio | Si en Development no se usa `Payments:Mode = Simulation`, se exigen secretos reales de Stripe; su uso en entornos de desarrollo aumenta el riesgo de filtración. |

---

## 4. FASE 2 — Análisis de autenticación y autorización

### 4.1 Middleware JWT

- **Program.cs:** `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)` con:
  - `ValidateIssuerSigningKey = true`, `IssuerSigningKey` desde el secreto validado al arranque.
  - `ValidateIssuer = true`, `ValidIssuer` desde `Jwt:Issuer` (default "movix").
  - `ValidateAudience = true`, `ValidAudience` desde `Jwt:Audience` (default "movix").
  - `ValidateLifetime = true`, `ClockSkew = TimeSpan.Zero` (sin tolerancia de expiración).
- **Orden del pipeline:** `UseAuthentication()` → `TenantMiddleware` → `UseAuthorization()` → `MapControllers()`. El tenant se resuelve después de autenticación, usando claims del JWT.

### 4.2 Políticas de autorización

- **Program.cs línea 70:** `AddAuthorization()` sin políticas nombradas. Solo se usan `[Authorize]` y `[Authorize(Roles = "X,Y")]`. No hay políticas personalizadas (ej. "RequireAdminOrSuperAdmin").

### 4.3 Endpoints sin protección [Authorize]

- **AuthController:** Sin `[Authorize]` a nivel de controlador: register, login, refresh, logout son públicos.
- **PaymentsController:** `[AllowAnonymous]` en POST `payments/webhook` y POST `payments/simulate-webhook`. El webhook depende de la validación de firma Stripe, no del JWT.
- **Resto de controladores:** Con `[Authorize]` a nivel de clase o de acción; requieren autenticación salvo las acciones marcadas AllowAnonymous.

### 4.4 Consistencia roles definidos vs usados

- **Dominio (Role enum):** Passenger, Driver, Admin, Support, SuperAdmin.
- **Controladores:** Se usan cadenas `"Admin,Support"`, `"Driver,Admin"`. **SuperAdmin no aparece en ningún `[Authorize(Roles = "...")]`**.
- **Handlers:** Varios comprueban `role == Role.SuperAdmin` o `_tenantContext.IsSuperAdmin` para permitir bypass de tenant (AssignDriver, CreatePayment, GetTrip, GetAdminTrips, GetAdminDrivers).
- **Riesgo:** Un usuario con rol **únicamente** SuperAdmin no pasaría la autorización en acciones con `[Authorize(Roles = "Admin,Support")]` (ej. assign-driver, admin/tariffs, admin/trips), porque ASP.NET Core evalúa la lista de roles y SuperAdmin no está incluida. Para que SuperAdmin pueda usar esas operaciones, debería tener también el rol Admin (o Support) en el token, o los controladores deberían incluir explícitamente "SuperAdmin" en Roles. No se ha comprobado en ejecución cómo se emiten los roles en el JWT (un solo rol vs varios).

---

## 5. FASE 3 — Análisis de máquina de estados

**Fuente:** `TripStateMachine.cs` (diccionario estático) y `TransitionTripCommandHandler` (único punto que cambia estado de viaje vía `CanTransition`).

### 5.1 Transiciones permitidas (por código)

- Requested → Accepted, Cancelled  
- Accepted → DriverArrived, Cancelled  
- DriverArrived → InProgress, Cancelled  
- InProgress → Completed, Cancelled  
- Completed → (ninguna)  
- Cancelled → (ninguna)  

### 5.2 Transiciones no validadas en otro lugar

- **AssignDriverCommandHandler:** Fuerza Requested → Accepted sin usar TripStateMachine; comprueba `trip.Status != TripStatus.Requested` y devuelve TRIP_INVALID_STATE. Coherente con la máquina.
- **AcceptTripCommandHandler:** Asigna Accepted (y DriverId/VehicleId); usa `CanTransition(trip.Status, Accepted)`. Solo Requested puede ir a Accepted; correcto.
- Cualquier otra transición (arrive, start, complete, cancel) pasa por `TransitionTripCommandHandler` y `CanTransition`. No se observan atajos que permitan saltar estados.

### 5.3 Posibles saltos de estado

- No se identifican en código. La única forma de cambiar el estado del viaje es mediante AssignDriver (Requested→Accepted) o TransitionTrip (resto). TransitionTrip rechaza con INVALID_TRANSITION si `CanTransition` es false.

### 5.4 Estados que podrían quedar inconsistentes

- **Complete sin DistanceKm/DurationMinutes:** Si se envía Complete sin body (o sin DistanceKm/DurationMinutes), el handler no calcula tarifa ni asigna FinalAmount (líneas 104-124 de TransitionTripCommandHandler). El viaje queda Completed con FinalAmount (y campos de tarifa) en null. El flujo de pago no valida que FinalAmount esté definido; solo que Status == Completed. Riesgo ya señalado en el reporte E2E anterior (pago con monto arbitrario o inconsistente).
- **Cancel después de Completed:** La máquina no permite transición desde Completed; el handler devolvería INVALID_TRANSITION. No hay inconsistencia por cancelación desde estado final.
- **DriverAvailability vs estado del viaje:** Al completar o cancelar, se limpia `CurrentTripId` del driver (líneas 125-133). Si fallara SaveChanges después de añadir el historial pero antes de commit, no se observa compensación explícita; el UnitOfWork hace un único SaveChanges. En caso de ConcurrencyException se lanza CONFLICT y no se confirma la transición; el estado en BD permanece coherente.

---

## 6. FASE 4 — Integridad financiera

### 6.1 Validación de pagos parciales

- **CreatePaymentCommandValidator:** Solo `Amount > 0` (y IdempotencyKey, TripId, Currency). No hay regla que exija `Amount == trip.FinalAmount` ni que prohíba `Amount > trip.FinalAmount`.
- **CreatePaymentCommandHandler:** No comprueba `request.Amount` frente a `trip.FinalAmount`. Se acepta cualquier monto positivo. Conclusión: pagos parciales y montos mayores al tarifado están permitidos por implementación.

### 6.2 Relectura dentro de transacción

- **CreatePaymentCommandHandler:** Lee el viaje una vez (`GetByIdAsync`), comprueba estado y passenger, luego crea el Payment y llama a `SaveChangesAsync`. No hay relectura del viaje ni del agregado de pagos dentro de una transacción explícita. La idempotencia se basa en IdempotencyKey (replay devuelve el mismo resultado); no en un lock optimista sobre "un pago por viaje".

### 6.3 Uso de token de concurrencia

- **Trip, Payment, DriverAvailability, User, etc.:** Tienen `RowVersion` configurado como `IsRowVersion()` / `IsConcurrencyToken()` en EF.
- **UnitOfWork:** Captura `DbUpdateConcurrencyException` y lanza `ConcurrencyException`. AssignDriver, AcceptTrip, TransitionTrip y DriverStatusCommandHandler capturan ConcurrencyException y devuelven error al cliente (ej. NO_DRIVERS_AVAILABLE, CONFLICT). El token de concurrencia se usa correctamente en las entidades revisadas.

### 6.4 Posibles race conditions

- **AssignDriver:** `GetFirstAvailableAsync` hace `Where(x => x.IsOnline && x.CurrentTripId == null).OrderBy(...).FirstOrDefaultAsync()` **sin bloqueo (sin FOR UPDATE SKIP LOCKED ni transacción de lectura)**. Dos requests simultáneos pueden obtener el mismo DriverAvailability; ambos asignan el mismo driver a dos viajes distintos y actualizan la misma fila. Al guardar, uno de los dos recibirá ConcurrencyException y devolverá NO_DRIVERS_AVAILABLE, pero la asignación "ganadora" puede no ser la esperada y el driver puede quedar asociado a un viaje mientras la otra actualización falla. No se ha verificado en BD si existe constraint o índice que impida lógica adicional.
- **CreatePayment (doble pago):** Con dos requests con **distintas** Idempotency-Key para el mismo TripId y mismo usuario, no hay comprobación "ya existe Payment para este TripId + PayerId". Ambos pueden crear un Payment; riesgo de doble cobro o inconsistencia de negocio.
- **Complete + Payment:** No hay bloqueo explícito sobre el viaje al crear el pago; dos clientes no deberían poder pagar el mismo viaje (solo el passenger), pero sí el mismo usuario con dos Idempotency-Keys distintas, como arriba.

---

## 7. FASE 5 — Riesgos potenciales

### 7.1 Escenarios que no pueden probarse ahora

- Cualquier flujo que requiera token JWT válido (login, refresh, creación de viaje, asignación, pago, etc.).
- Comportamiento real de RequireTenant cuando el JWT no trae tenant_id (usuario Passenger/Driver sin claim).
- Diferenciación 404 vs 403 en GetTrip para Admin/Support cuando el viaje es de otro tenant (enumeración por GUID).
- Webhook de Stripe (firma, idempotencia por event id).
- Rate limiting (auth, trips, payments) bajo carga.
- Health checks (PostgreSQL, Redis, Outbox) con servicios caídos.
- Seed: creación de tenant dev y usuarios Admin/Driver cuando las variables están definidas.
- Migraciones aplicadas sobre una BD vacía o con datos previos.

### 7.2 Escenarios que deben probarse cuando el entorno esté listo

- **Crítico:** Pago con Amount > trip.FinalAmount y pago con Amount < trip.FinalAmount (parcial); doble pago mismo viaje/usuario con dos Idempotency-Keys.
- **Crítico:** Dos assign-driver simultáneos (mismo o distintos viajes) y verificar que un solo conductor queda asignado por disponibilidad y que no hay doble asignación del mismo driver.
- **Alto:** Acceso con token expirado; acceso sin token a endpoints protegidos; uso de rol SuperAdmin (con o sin Admin) en assign-driver y admin.
- **Alto:** Complete sin DistanceKm/DurationMinutes y luego intento de pago; validar si el negocio lo permite y si el monto del pago está acotado.
- **Medio:** RequireTenant en Create/Cancel/AssignDriver con tenant inválido o ausente; X-Tenant-Id inválido para SuperAdmin (TENANT_INVALID).
- **Medio:** Transiciones inválidas (Completed → Cancelled, Requested → Completed, etc.) y verificar código 422 INVALID_TRANSITION.
- **Medio:** IDOR: GET trip con GUID de otro tenant (como Admin) y verificar respuesta (404 vs 403).

### 7.3 Riesgos de nivel alto (resumen)

- Doble pago (distintas Idempotency-Keys) sin restricción por (TripId, PayerId).
- Monto de pago no validado frente a FinalAmount (exceso o parcial no definido).
- Race en GetFirstAvailableAsync sin bloqueo, con posible doble asignación del mismo driver hasta que una escritura falle por concurrencia.
- SuperAdmin no incluido en [Authorize(Roles)] en endpoints de admin, pudiendo impedir el uso previsto del rol.
- Secreto JWT configurable desde appsettings (no forzado desde variable de entorno), riesgo de exposición si se versiona el archivo.

---

## 8. FASE 6 — Requisitos para habilitar E2E reales

### 8.1 Variables de entorno requeridas

| Variable (ejemplo .NET) | Obligatoria | Descripción |
|--------------------------|-------------|-------------|
| `Jwt__SecretKey` | Sí (salvo Simulation) | Secreto para firmar/validar JWT. Mínimo 32 caracteres. No debe contener "CHANGE_ME". |
| `Stripe__SecretKey` | Si Payments:Mode ≠ Simulation | Clave API Stripe. |
| `Stripe__WebhookSecret` | Si Payments:Mode ≠ Simulation | Secreto del webhook Stripe. |
| `ADMIN_EMAIL` | Para seed Admin | Email del usuario Admin de desarrollo. |
| `ADMIN_PASSWORD` | Para seed Admin | Contraseña del Admin. |
| `DRIVER_EMAIL` | Para seed Driver | Email del usuario Driver. |
| `DRIVER_PASSWORD` | Para seed Driver | Contraseña del Driver. |
| `ConnectionStrings__DefaultConnection` | Sí | Cadena de conexión PostgreSQL (puede venir de appsettings). |
| `ConnectionStrings__Redis` | Recomendado | Redis para idempotencia (y posiblemente otros usos). |

### 8.2 Estructura mínima de base de datos

- Base PostgreSQL creada y accesible.
- Cadena de conexión con permisos para crear/escribir esquema.
- La aplicación ejecuta `Database.MigrateAsync()` al arranque; no es necesario aplicar migraciones a mano si se permite que la app migre.
- Redis accesible si se usa idempotencia en Redis (recomendado para E2E de pagos/trips con Idempotency-Key).

### 8.3 Usuario seed necesario

- **Tenant:** DataSeeder crea tenant con Id `00000000-0000-0000-0000-000000000001` ("Dev Tenant") solo en entorno Development.
- **Admin:** Un usuario con rol Admin y TenantId = DevTenantId, si están definidos `ADMIN_EMAIL` y `ADMIN_PASSWORD`.
- **Driver:** Un usuario con rol Driver, TenantId = DevTenantId, con un Driver y un Vehicle, si están definidos `DRIVER_EMAIL` y `DRIVER_PASSWORD`.
- **Passenger:** No se seedea; debe crearse vía POST auth/register con TenantId = DevTenantId. Para E2E hace falta al menos un Passenger registrado y un Admin (y opcionalmente un Driver) para cubrir flujos por rol.
- **SuperAdmin:** No hay seed de SuperAdmin; habría que insertarlo manualmente en BD o tener un flujo de elevación no revisado en este análisis.

### 8.4 Secreto mínimo requerido

- Longitud ≥ 32 caracteres.
- No vacío, no solo espacios, no contener la cadena "CHANGE_ME" (case-insensitive).
- Recomendación de auditoría: que el valor provenga únicamente de variable de entorno en producción y no de appsettings versionados.

### 8.5 Configuración mínima para levantar entorno

- **appsettings / env:** Jwt:SecretKey (≥32 chars); ConnectionStrings:DefaultConnection (y Redis si aplica); si no es Simulation, Stripe:SecretKey y Stripe:WebhookSecret.
- **Entorno:** ASPNETCORE_ENVIRONMENT=Development para que el seed se ejecute.
- **Seed (recomendado para E2E):** ADMIN_EMAIL, ADMIN_PASSWORD, DRIVER_EMAIL, DRIVER_PASSWORD definidos.
- **Payments:** Para evitar Stripe en desarrollo, configurar `Payments:Mode = Simulation` (si existe esta rama en código; en Program.cs solo se comprueba que no sea "Simulation" para exigir Stripe).
- **Puertos:** La API usa los definidos en launchSettings (ej. https://localhost:55391). PostgreSQL y Redis en los puertos indicados en la cadena de conexión.

---

## 9. Checklist para habilitar pruebas reales

- [ ] Definir `Jwt__SecretKey` (≥32 caracteres, no "CHANGE_ME") en entorno o en archivo no versionado.
- [ ] Configurar `ConnectionStrings__DefaultConnection` para PostgreSQL accesible.
- [ ] Configurar `ConnectionStrings__Redis` si la idempotencia usa Redis.
- [ ] Si no se usa Simulation: definir `Stripe__SecretKey` y `Stripe__WebhookSecret`.
- [ ] Ejecutar la API en entorno Development para que se ejecute el seed.
- [ ] Definir `ADMIN_EMAIL` y `ADMIN_PASSWORD` (y opcionalmente `DRIVER_EMAIL`, `DRIVER_PASSWORD`) para tener usuarios por rol.
- [ ] Crear al menos un Passenger vía POST auth/register (TenantId = DevTenantId).
- [ ] (Opcional) Poner el Driver en línea (POST drivers/status) y asegurar DriverAvailability para assign-driver.
- [ ] Crear al menos un TariffPlan activo para el tenant dev si se van a probar quote y complete con tarifa.
- [ ] Verificar que /health y /ready responden según lo esperado una vez BD y Redis estén disponibles.
- [ ] Ejecutar los escenarios listados en la sección 7.2 (pagos, doble asignación, SuperAdmin, transiciones, IDOR, etc.).

---

## 10. Nivel de confianza actual del sistema

| Área | Nivel | Comentario |
|------|--------|------------|
| Arranque y configuración | Medio | Validación fuerte del JWT SecretKey y longitud; riesgo si el secreto viene de appsettings versionado. Stripe y BD obligatorios según modo. |
| Autenticación | Medio | JWT con validación de firma, issuer, audience y lifetime con ClockSkew cero. No se ha probado en ejecución; SuperAdmin no incluido en Roles en controladores. |
| Autorización por rol | Medio-bajo | Coherencia entre handlers y tenant; posible gap SuperAdmin en [Authorize]. Endpoints sensibles protegidos por rol; falta validación E2E. |
| Máquina de estados | Alto | Transiciones centralizadas en TripStateMachine y validadas en TransitionTripCommandHandler; no se ven saltos de estado. Riesgo: Complete sin tarifa (FinalAmount null). |
| Integridad financiera | Bajo | Sin validación Amount vs FinalAmount; sin restricción "un pago por (TripId, PayerId)"; idempotencia solo por clave. ConcurrencyException y RowVersion presentes pero doble pago con distinta clave posible. |
| Concurrencia | Medio-bajo | RowVersion y ConcurrencyException usados; AssignDriver puede asignar el mismo driver a dos viajes hasta que uno falle por concurrencia. Outbox con FOR UPDATE SKIP LOCKED en PostgreSQL documentado en otro informe. |
| Tenant y aislamiento | Medio | RequireTenant y validaciones en handlers; GetTrip devuelve TRIP_NOT_FOUND para otro tenant (riesgo de enumeración). Sin E2E no se puede afirmar que no haya fugas. |

**Conclusión:** El nivel de confianza global se considera **medio-bajo** para desplegar en producción sin haber ejecutado E2E y sin abordar: (1) validación de monto de pago frente a FinalAmount y restricción de un pago por (TripId, PayerId), (2) mitigación de race en GetFirstAvailableAsync (bloqueo o transacción), (3) inclusión explícita de SuperAdmin en autorización donde corresponda, (4) decisión sobre Complete sin DistanceKm/DurationMinutes y (5) refuerzo de que el secreto JWT provenga solo de variable de entorno en producción. No se suavizan hallazgos; como auditor externo se asume que hasta no ejecutar las pruebas y corregir los puntos anteriores, el sistema no debe considerarse validado para contexto transaccional crítico.
