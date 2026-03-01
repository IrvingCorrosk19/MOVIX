# MOVIX — Decisiones de arquitectura (DECISIONS)

## ADR-001: Clean Architecture y Modular Monolith

**Contexto:** Necesidad de un backend mantenible, auditable y preparado para crecer multi-ciudad/país y eventual migración a microservicios.

**Decisión:** Adoptar Clean Architecture con cuatro proyectos: Domain, Application, Infrastructure, Api. El sistema se estructura como monolito modular (módulos por dominio: Auth, Trips, Drivers, Payments, Admin) con fronteras claras para poder extraer servicios más adelante.

**Consecuencias:** Las dependencias apuntan hacia el dominio (Application y Infrastructure dependen de Domain; Api depende de Application e Infrastructure). Las interfaces de persistencia y servicios externos viven en Application; las implementaciones en Infrastructure.

---

## ADR-002: Máquina de estados para Trips

**Contexto:** Las transiciones de estado de un viaje deben ser explícitas y auditables para cumplir normativa y soporte.

**Decisión:** Introducir una máquina de estados formal en Domain (`TripStateMachine`) que define las transiciones permitidas: Requested -> Accepted | Cancelled; Accepted -> DriverArrived | Cancelled; DriverArrived -> InProgress | Cancelled; InProgress -> Completed | Cancelled. Cualquier cambio de estado se persiste en `trip_status_history` de forma inmutable.

**Consecuencias:** Los handlers de comandos (Accept, Arrive, Start, Complete, Cancel) validan la transición antes de aplicar el cambio. Se evitan estados inválidos y se dispone de historial completo para auditoría.

---

## ADR-003: Idempotencia con Redis y header Idempotency-Key

**Contexto:** Creación de viajes y pagos debe ser idempotente para evitar duplicados en reintentos o doble clic.

**Decisión:** Para `POST /api/v1/trips` y `POST /api/v1/payments`, el header `Idempotency-Key` es obligatorio. El valor se usa como clave en Redis para almacenar la respuesta ya generada (por ejemplo, el ID del recurso). Si la clave existe, se devuelve la respuesta almacenada sin re-ejecutar la lógica de negocio. TTL de 24 horas para las claves.

**Consecuencias:** Los clientes deben generar y reenviar la misma clave en reintentos. Se reduce carga en BD y se garantiza consistencia en operaciones críticas.

---

## ADR-004: JWT corto + Refresh token rotativo con detección de reuse

**Contexto:** Seguridad de sesión y cumplimiento de buenas prácticas (tokens de corta duración, refresh rotativo).

**Decisión:** Access token JWT con vida corta (por ejemplo 15 minutos). Refresh token opaco, almacenado hasheado en BD con fecha de expiración y revocación. En cada refresh se emite un nuevo refresh token, se revoca el anterior y se registra `ReplacedByTokenId`. Si se recibe un refresh token ya reemplazado, se considera reuse y se rechaza (posible robo de token).

**Consecuencias:** Se limita la ventana de uso de un token robado y se puede invalidar familias de refresh tokens en caso de detección de reuse.

---

## ADR-005: Patrón Outbox (tabla)

**Contexto:** Necesidad de publicar eventos o mensajes a otros sistemas (notificaciones, analytics) sin perder consistencia con la transacción de negocio.

**Decisión:** Implementar tabla `outbox_messages` (Type, Payload, CreatedAtUtc, ProcessedAtUtc, Error). Los handlers que deban emitir eventos escriben en esta tabla en la misma transacción que el cambio de negocio. Un proceso en background (pendiente de implementación) lee mensajes no procesados y los publica (por ejemplo a un bus o cola), actualizando `ProcessedAtUtc` o `Error`.

**Consecuencias:** Garantía at-least-once y consistencia con la base de datos. La implementación del worker de outbox queda como siguiente paso.

---

## ADR-006: Auditoría y UTC

**Contexto:** Trazabilidad y cumplimiento (ej. ISO 27001).

**Decisión:** Todas las entidades de negocio con auditoría implementan `IAuditableEntity` (CreatedAtUtc, UpdatedAtUtc, CreatedBy, UpdatedBy). Todas las fechas se almacenan en UTC (timestamptz en PostgreSQL). Un interceptor de EF Core rellena automáticamente estos campos usando `ICurrentUserService` cuando está disponible.

**Consecuencias:** Consultas y reportes deben interpretar siempre en UTC; la presentación en zona horaria local se hace en cliente o en una capa de presentación.

---

## ADR-007: RBAC + ABAC

**Contexto:** Control de acceso por rol y por propietario del recurso.

**Decisión:** RBAC con roles Passenger, Driver, Admin, Support aplicado en controladores con `[Authorize(Roles = "...")]`. ABAC para recursos como un viaje: GET trip solo permitido si el usuario es el pasajero, el conductor asignado o tiene rol Admin/Support; la comprobación se hace en el handler de la query (GetTripQueryHandler).

**Consecuencias:** Los controladores quedan simples; la lógica de “quién puede ver/actuar sobre qué” se centraliza en la capa Application.

---

## ADR-008: Rate limiting por endpoint

**Contexto:** Protección frente a abuso en login, creación de viajes y pagos.

**Decisión:** Usar el rate limiter de ASP.NET Core con políticas fijas: "auth" (login/refresh), "trips", "payments" con límites por ventana (por ejemplo 10/30/20 por minuto). Aplicado con `[EnableRateLimiting("policy")]` en los endpoints correspondientes.

**Consecuencias:** En entornos de múltiples instancias se deberá usar un almacén distribuido (por ejemplo Redis) para el rate limiter si se requiere límite global.

---

## ADR-009: Observabilidad (Serilog, Health, OpenTelemetry)

**Contexto:** Operación en producción y diagnóstico de incidencias.

**Decisión:** Serilog para logs estructurados con prefijo "movix". Correlation ID en cada request (middleware) e incluido en el scope de log. Health checks para PostgreSQL y Redis en `/health` y `/ready`. OpenTelemetry habilitado para tracing (AspNetCore, HttpClient) y métricas básicas; export OTLP configurable para enviar a un colector externo.

**Consecuencias:** No incluir PII en mensajes de log; usar propiedades estructuradas para IDs cuando sea necesario para correlación.

---

## ADR-010: PostgreSQL + PostGIS para datos geoespaciales

**Contexto:** Ubicaciones de pickup/dropoff y posiciones en vivo de conductores.

**Decisión:** PostgreSQL con extensión PostGIS. Tipos `geometry` (Point) para `trips.PickupLocation`, `trips.DropoffLocation` y `driver_location_live.Location`. NetTopologySuite en Domain y EF Core con Npgsql.NetTopologySuite para mapeo. Índices espaciales (GIST) recomendados para consultas por proximidad (pendiente en migraciones).

**Consecuencias:** Consultas geoespaciales (cercanía, rutas) se pueden implementar en SQL/EF sin servicios externos. Escalado multi-ciudad/país se puede apoyar en particionamiento o esquemas por región en el futuro.
