# Reporte de Pruebas Funcionales E2E — MOVIX API

**Clasificación:** QA Senior — Sistemas críticos (financieros/transaccionales)  
**Alcance:** API REST ASP.NET Core, autenticación JWT, roles, lógica financiera y estados de viajes.  
**Metodología:** Análisis estático de código, documentación de arquitectura y diseño de casos de prueba. No se modificó código.

**Nota sobre ejecución:** Las pruebas E2E en vivo no pudieron ejecutarse: la API requiere `JWT SecretKey` (variable de entorno, mínimo 32 caracteres) y base de datos configurada. Este reporte se basa en análisis del código, handlers, filtros, contratos y documentación existente. Los hallazgos se derivan de revisión de implementación y de escenarios que deben validarse cuando el entorno esté disponible.

**Base URL de referencia (Development):** `https://localhost:55391` / `http://localhost:55392`

---

## 1. Resumen Ejecutivo

Se realizó descubrimiento de endpoints, roles, autenticación y flujos sensibles, y se analizaron handlers y reglas de negocio para identificar riesgos y comportamientos esperados. Se documentan hallazgos críticos (integridad financiera, validación de monto vs saldo), medios (IDOR por GUID, overposting) y menores (mensajes de error, consistencia de códigos). Se confirman varias validaciones correctas (estado del viaje para pago, tenant, idempotencia de pago, máquina de estados). Se recomienda ejecutar los casos de prueba diseñados en un entorno con API y datos de prueba y corregir los puntos críticos antes de producción.

---

## 2. FASE 1 — Descubrimiento

### 2.1 Endpoints disponibles (mapeo)

| # | Método | Ruta | Autenticación | Roles | RequireTenant | Sensible |
|---|--------|------|---------------|-------|---------------|----------|
| 1 | POST | api/v1/auth/register | No | Público | No | No |
| 2 | POST | api/v1/auth/login | No | Público | No | No |
| 3 | POST | api/v1/auth/refresh | No | Público | No | No |
| 4 | POST | api/v1/auth/logout | No | Público | No | No |
| 5 | POST | api/v1/trips | Sí | Cualquiera | Sí | Sí (creación) |
| 6 | GET | api/v1/trips/{id} | Sí | Owner / Admin, Support, SuperAdmin | No | No |
| 7 | POST | api/v1/trips/{id}/assign-driver | Sí | Admin, Support | Sí | **Sí (reasignación)** |
| 8 | POST | api/v1/trips/{id}/accept | Sí | Driver, Admin | No | Sí |
| 9 | POST | api/v1/trips/{id}/arrive | Sí | Driver, Admin | Sí | No |
| 10 | POST | api/v1/trips/{id}/start | Sí | Driver, Admin | Sí | No |
| 11 | POST | api/v1/trips/{id}/complete | Sí | Driver, Admin | No | Sí |
| 12 | POST | api/v1/trips/{id}/cancel | Sí | Passenger, Driver, Admin, Support | Sí | **Sí (cancelación)** |
| 13 | POST | api/v1/payments | Sí | Cualquiera | No | **Sí (pagos)** |
| 14 | POST | api/v1/payments/webhook | No (AllowAnonymous) | — | No | **Sí** |
| 15 | POST | api/v1/payments/simulate-webhook | No (AllowAnonymous) | — | No | Sí (solo Dev) |
| 16 | POST | api/v1/admin/tariffs | Sí | Admin, Support | Sí | No |
| 17 | PUT | api/v1/admin/tariffs/{id} | Sí | Admin, Support | Sí | No |
| 18 | POST | api/v1/admin/tariffs/{id}/activate | Sí | Admin, Support | Sí | No |
| 19 | GET | api/v1/admin/tariffs/active | Sí | Admin, Support | Sí | No |
| 20 | GET | api/v1/admin/tariffs/fare/quote | Sí | Admin, Support | Sí | No |
| 21 | GET | api/v1/fare/quote | Sí | Cualquiera | Sí | No |
| 22 | POST | api/v1/admin/tenants | Sí | Admin, Support | No | No |
| 23 | GET | api/v1/admin/tenants | Sí | Admin, Support | No | No |
| 24 | GET | api/v1/admin/trips | Sí | Admin, Support | No | No |
| 25 | GET | api/v1/admin/drivers | Sí | Admin, Support | No | No |
| 26 | POST | api/v1/admin/outbox/{id}/reprocess | Sí | Admin, Support | No | Sí (ops) |
| 27 | GET | api/v1/admin/ops/payments | Sí | Admin, Support | No | Sí (datos) |
| 28 | GET | api/v1/admin/ops/outbox | Sí | Admin, Support | No | Sí (ops) |
| 29 | POST | api/v1/admin/ops/outbox/{id}/reprocess | Sí | Admin, Support | No | Sí (ops) |
| 30 | POST | api/v1/drivers/onboarding | Sí | Driver, Admin | No | No |
| 31 | POST | api/v1/drivers/status | Sí | Driver, Admin | No | No |
| 32 | POST | api/v1/drivers/location | Sí | Driver, Admin | No | No |

### 2.2 Endpoints que requieren autenticación

Todos excepto: `auth/register`, `auth/login`, `auth/refresh`, `auth/logout`, `payments/webhook`, `payments/simulate-webhook`.

### 2.3 Roles por endpoint (resumen)

- **Cualquiera autenticado:** POST trips, POST payments, GET fare/quote, GET trips/{id} (si owner).
- **Admin, Support:** assign-driver, admin/tariffs/*, admin/tenants, admin/trips, admin/drivers, admin/ops/*, outbox reprocess.
- **Driver, Admin:** accept, arrive, start, complete, drivers/onboarding, drivers/status, drivers/location.
- **Passenger / Driver / Admin / Support:** cancel (ABAC: owner del viaje o Admin/Support).

### 2.4 Endpoints sensibles identificados

- **Pagos:** POST api/v1/payments (idempotency key obligatoria).  
- **Cancelación:** POST api/v1/trips/{id}/cancel.  
- **Reasignación:** POST api/v1/trips/{id}/assign-driver.  
- **Webhook:** POST api/v1/payments/webhook (AllowAnonymous; validación por firma Stripe).  
- **Ops:** admin/ops/payments, admin/ops/outbox, reprocess.

---

## 3. Hallazgos Críticos

| ID | Severidad | Impacto | Evidencia | Descripción |
|----|-----------|---------|-----------|-------------|
| H-01 | Crítico | Financiero | CreatePaymentCommandHandler.cs | **Falta validación Amount vs FinalAmount del viaje.** El handler valida `Amount > 0` (validator) y que el viaje esté Completed y sea del passenger, pero no comprueba que `request.Amount <= trip.FinalAmount`. Un cliente podría enviar un monto mayor al saldo del viaje (ej. FinalAmount=10, Amount=999999) y crear un Payment con ese monto. El gateway externo podría rechazarlo, pero la entidad Payment y el outbox quedarían con un monto incoherente con el viaje. |
| H-02 | Crítico | Financiero | CreatePaymentCommandHandler.cs | **Doble pago con distinta Idempotency-Key.** La idempotencia está ligada a la clave, no al par (TripId, UserId). Dos requests con distinto Idempotency-Key para el mismo viaje y mismo usuario pueden crear dos Payments. No existe validación "ya existe un pago (Pending/Confirmed) para este TripId y PayerId". Riesgo de doble cobro o inconsistencia. |
| H-03 | Crítico | Datos/Seguridad | GetTripQueryHandler.cs (líneas 41–44) | **IDOR por GUID: Admin/Support reciben TRIP_NOT_FOUND en lugar de FORBIDDEN.** Para Admin/Support que no son owner, si el viaje es de otro tenant se devuelve "Trip not found" (y 404). Un atacante puede inferir existencia de viajes probando GUIDs: 404 = no existe o otro tenant, 200/403 = existe. Debería evaluarse si para cross-tenant se responde 403 con mensaje genérico para no revelar existencia. |

---

## 4. Hallazgos Medios

| ID | Severidad | Impacto | Evidencia | Descripción |
|----|-----------|---------|-----------|-------------|
| H-04 | Alto | Seguridad | Contratos API (CreatePaymentRequest, CreateTripRequest, etc.) | **Overposting:** Los DTOs son records con campos fijos. Cualquier campo extra en JSON es ignorado por el deserializador por defecto. No hay lista explícita de "allowed" en el controller. Si en el futuro se añaden propiedades a entidades y se mapean desde el request sin restricción, podría haber overposting. Actualmente el riesgo es bajo por uso de records cerrados. |
| H-05 | Alto | UX/Datos | Varios handlers | **Acceso sin token:** Los endpoints protegidos con [Authorize] devuelven 401 cuando no hay token. Debe comprobarse en E2E que no se filtre información sensible en el cuerpo de la respuesta (ej. stack trace o detalles internos). |
| H-06 | Medio | Financiero | CreatePaymentCommandHandler.cs | **Pago con Amount &lt; FinalAmount (parcial):** No hay regla que exija Amount == trip.FinalAmount. Se permiten pagos parciales (Amount &lt; FinalAmount). Si el negocio requiere "un solo pago por el total", falta esta validación; si se permiten parciales, falta definir flujo de saldo pendiente y posibles pagos adicionales. |
| H-07 | Medio | Estados | TransitionTripCommandHandler.cs | **Complete sin DistanceKm/DurationMinutes:** Si se completa un viaje sin enviar DistanceKm y DurationMinutes, no se calcula FinalAmount (no se llama a GetApplicableTariffAsync ni se asigna tarifa). El viaje queda Completed con FinalAmount/tarifa en null. Luego POST payments exige trip.Completed pero no que FinalAmount esté definido; el usuario podría intentar pagar un monto arbitrario (ya cubierto en H-01/H-06). |

---

## 5. Hallazgos Menores

| ID | Severidad | Impacto | Evidencia | Descripción |
|----|-----------|---------|-----------|-------------|
| H-08 | Bajo | UX | Endpoints que devuelven 400 | Algunos errores se mapean a 400 con código genérico. Unificar códigos (ej. UNAUTHORIZED, TRIP_NOT_FOUND) y mensajes para facilitar integración frontend y soporte. |
| H-09 | Bajo | Seguridad | payments/webhook | Webhook es AllowAnonymous; la protección es la firma Stripe. Validar en E2E que requests sin firma válida reciben 400 y que no se procesan eventos. |
| H-10 | Bajo | Datos | Cancel / Complete | Cancelar un viaje no borra datos; Completed y Cancelled son finales. No se ha verificado si hay restricciones de eliminación o anonimización (ej. RGPD) en otros flujos. |

---

## 6. Riesgos Potenciales (sin evidencia directa en código)

- **Concurrencia — 50 órdenes simultáneas / 4 técnicos:** CreateTrip usa Idempotency-Key; AssignDriver toma el primer disponible y usa concurrencia a nivel de UnitOfWork. Riesgo de race si dos admins asignan al mismo tiempo (NO_DRIVERS_AVAILABLE o ConcurrencyException). Los tests de integración de Outbox usan FOR UPDATE SKIP LOCKED en PostgreSQL; el resto de flujos no tienen locks explícitos en el reporte.  
- **Doble procesamiento de pago:** Mitigado por idempotency por clave; el riesgo de doble pago con claves distintas queda como H-02.  
- **Token expirado:** Comportamiento estándar de JWT (401). Validar en E2E que no se devuelve contenido sensible y que el cliente puede usar refresh.  
- **Saldo negativo:** No existe concepto de "saldo" en cuenta en el código revisado; los pagos son por viaje. El riesgo "nunca saldo negativo" aplicaría a un agregado de pagos por usuario/tenant que no está implementado en los handlers revisados.

---

## 7. Validaciones Correctas (confirmadas por código)

- **Pago solo si viaje Completed:** CreatePaymentCommandHandler exige `trip.Status == TripStatus.Completed`; si no, TRIP_NOT_COMPLETED.  
- **Pago solo por el passenger:** `trip.PassengerId != userId` → UNAUTHORIZED_PAYMENT.  
- **Tenant en pago:** Si no SuperAdmin y trip.TenantId != TenantContext.TenantId → FORBIDDEN.  
- **Idempotency-Key obligatoria en POST payments y POST trips:** Controller devuelve 400 IDEMPOTENCY_KEY_REQUIRED si falta.  
- **Replay de misma clave en pago:** GetResponseAsync + StoreAsync; mismo Idempotency-Key devuelve el mismo PaymentDto sin crear otro.  
- **Máquina de estados de viaje:** TripStateMachine define transiciones permitidas; TransitionTripCommandHandler usa CanTransition; transiciones inválidas devuelven INVALID_TRANSITION.  
- **Estados finales:** Completed y Cancelled no tienen transiciones salida; no se puede "reabrir" desde código.  
- **AssignDriver — tenant:** Trip y driver deben ser del mismo tenant (salvo SuperAdmin); si no, FORBIDDEN.  
- **AcceptTrip — tenant:** trip.TenantId debe coincidir con driver.TenantId; si no, FORBIDDEN.  
- **GetTrip — ABAC:** Solo owner (passenger/driver) o Admin/Support/SuperAdmin; Admin/Support restringidos a tenant (TRIP_NOT_FOUND para otro tenant).  
- **Cancel — ABAC:** Solo passenger, driver del viaje o Admin/Support.  
- **Amount > 0 en pago:** CreatePaymentCommandValidator RuleFor(x => x.Amount).GreaterThan(0).

---

## 8. FASE 2 — Pruebas por rol (diseño de casos; ejecutar con API levantada)

### 8.1 Passenger (Customer)

| Paso | Acción | Resultado esperado | Verificar |
|-----|--------|--------------------|-----------|
| 1 | POST auth/login (email, password) | 200, AccessToken, RefreshToken | Token en header Authorization |
| 2 | POST trips (con Idempotency-Key, tenant en JWT/context) | 200, TripDto Status=Requested | Trip creado, TenantId y PassengerId correctos |
| 3 | PUT/PATCH para "modificar" trip | No existe endpoint de actualización de trip en la documentación | 404 o 405 |
| 4 | POST trips/{id}/cancel (siendo passenger) | 200, Status=Cancelled | Solo si estado permite Cancel (Requested, Accepted, DriverArrived, InProgress) |
| 5 | POST payments (trip cancelado, mismo Idempotency-Key que antes) | 400 TRIP_NOT_COMPLETED | No crear Payment |
| 6 | GET trips/{id} con id de viaje de otro usuario (mismo tenant o otro) | 403 FORBIDDEN o 404 (si otro tenant y admin) | No devolver datos del viaje ajeno |

### 8.2 Admin

| Paso | Acción | Resultado esperado | Verificar |
|-----|--------|--------------------|-----------|
| 1 | POST auth/login (Admin) | 200, token con role Admin | RequireTenant en endpoints que lo usan |
| 2 | GET admin/trips (con tenant en contexto) | 200, lista de viajes del tenant | ApplyTenantScope aplicado |
| 3 | POST trips/{id}/complete o cancel en viaje ya Completed | 400 INVALID_TRANSITION | No cambiar estado final |
| 4 | POST trips/{id}/assign-driver (trip Requested) | 200 o 409 NO_DRIVERS_AVAILABLE | Driver del mismo tenant; Outbox + Audit |
| 5 | Revisar audit_logs / outbox (si hay endpoint o DB) | Registros coherentes con acciones | No modificar código; solo reportar si se ve inconsistencia |

### 8.3 Driver (Technician)

| Paso | Acción | Resultado esperado | Verificar |
|-----|--------|--------------------|-----------|
| 1 | POST auth/login (Driver) | 200 | |
| 2 | GET trips/{id} con id de viaje asignado a otro driver | 403 FORBIDDEN (si no es owner ni Admin) | No acceso a viaje de otro driver |
| 3 | POST trips/{id}/accept (si Driver asignado vía assign-driver, mismo driver) | 200, Status=Accepted | VehicleId en body |
| 4 | POST trips/{id}/arrive luego start luego complete | 200 en cada paso según estado | Transiciones válidas |
| 5 | POST trips/{id}/complete de nuevo (ya Completed) | 400 INVALID_TRANSITION | No completed → pending ni re-complete |

---

## 9. FASE 3 — Pruebas de estados (máquina de estados)

Transiciones definidas en TripStateMachine:

- **Requested** → Accepted, Cancelled  
- **Accepted** → DriverArrived, Cancelled  
- **DriverArrived** → InProgress, Cancelled  
- **InProgress** → Completed, Cancelled  
- **Completed** → (ninguna)  
- **Cancelled** → (ninguna)

Casos a ejecutar en E2E:

- Requested → Completed (inválido) → 422 INVALID_TRANSITION.  
- Completed → Cancelled (inválido) → 422 INVALID_TRANSITION.  
- Cancelled → InProgress (inválido) → 422 INVALID_TRANSITION.  
- DriverArrived → Accepted (inválido) → 422 INVALID_TRANSITION.  
- Pending → Paid → Completed: en MOVIX no existe estado "Paid" del viaje; el flujo es InProgress → Completed y luego POST payments. No hay "Paid" como estado de trip.  
- Paid → Cancelled: no aplica; el cancel es sobre el trip, no sobre un estado "Paid" del trip.

---

## 10. FASE 4 — Pruebas de integridad financiera (diseño)

| Caso | Acción | Resultado esperado | Estado en código |
|------|--------|--------------------|------------------|
| Pago mayor al saldo | POST payments con Amount &gt; trip.FinalAmount | Ideal: 400 (ej. AMOUNT_EXCEEDS_FARE). Actual: no validado en handler | **H-01** |
| Doble pago misma clave | POST payments dos veces con mismo Idempotency-Key | 200 ambas con mismo PaymentDto | Idempotency implementado |
| Doble pago distinta clave | POST payments dos veces mismo TripId, mismo usuario, distinta Idempotency-Key | Actual: posible crear dos Payments | **H-02** |
| Pago con saldo 0 | No aplicable tal cual: no hay "saldo" de cuenta; el viaje tiene FinalAmount. Pago con Amount=0 | 400 (validator Amount &gt; 0) | Validado |
| Cancelar con "saldo pendiente" | Trip InProgress → cancel. ¿Permitido? | Sí (InProgress → Cancelled). No hay concepto "saldo pendiente" por trip antes de Complete | Correcto |
| Saldo negativo | No hay agregado de saldo en código | N/A | Riesgo potencial si se añade lógica de saldo |

---

## 11. FASE 5 — Seguridad (diseño de casos)

- **IDOR por GUID:** GET trips/{id} con id aleatorio o de otro tenant. Esperado: 404 o 403. Riesgo: ver H-03 (diferenciación 404 vs 403 para Admin).  
- **Falta de validación de rol:** AssignDriver solo Admin/Support; si se envía token de Passenger, 403. Probar con cada rol en endpoints restringidos.  
- **Overposting:** Enviar JSON con campos extra (ej. IsAdmin: true) en CreatePaymentRequest / CreateTripRequest. Esperado: campos ignorados (records). No debe persistirse IsAdmin.  
- **Acceso sin token:** GET/POST en endpoints [Authorize] sin header Authorization. Esperado: 401.  
- **Token expirado:** Mismo request con token expirado. Esperado: 401. No debe devolverse cuerpo con datos sensibles.

---

## 12. FASE 6 — Concurrencia (simulación / revisión)

- **50 órdenes simultáneas:** CreateTrip con 50 Idempotency-Keys distintas; esperado: 50 viajes creados sin corrupción. Posible contención en DB/transacciones.  
- **4 técnicos (drivers):** Varios assign-driver simultáneos; GetFirstAvailableAsync puede asignar el mismo driver si no hay lock (riesgo de race). Revisar si hay transacción y bloqueo por fila.  
- **Pagos concurrentes:** Mismo TripId, mismo usuario, misma Idempotency-Key en paralelo: solo un Payment y respuestas idénticas. Distinta Idempotency-Key: riesgo H-02.  
- **Reasignaciones simultáneas:** Dos assign-driver al mismo trip en paralelo: uno debe ganar, el otro TRIP_INVALID_STATE o ConcurrencyException/NO_DRIVERS_AVAILABLE según implementación.

---

## 13. Recomendaciones (sin modificar código en este reporte)

1. **H-01:** Añadir en el handler de pago (o en un validator de aplicación) la regla: si `trip.FinalAmount.HasValue` y `request.Amount > trip.FinalAmount.Value`, rechazar con código tipo AMOUNT_EXCEEDS_FARE.  
2. **H-02:** Considerar restricción "un solo pago exitoso (o Pending) por (TripId, PayerId)" además de idempotency por clave; rechazar segundo pago con código DUPLICATE_PAYMENT.  
3. **H-03:** Valorar devolver 403 con mensaje genérico cuando Admin/Support consultan un trip de otro tenant, para no revelar existencia del recurso (evitar enumeración por GUID).  
4. **H-06:** Definir si el negocio permite pagos parciales; si no, validar Amount == trip.FinalAmount (con tolerancia decimal si aplica).  
5. **H-07:** Definir si Complete sin DistanceKm/DurationMinutes es válido; si no, rechazar con INVALID_COMPLETION_DATA o exigir siempre ambos para calcular tarifa.  
6. **E2E:** Configurar entorno con JWT SecretKey y DB (y opcionalmente datos seed: tenant, usuario Passenger, Driver, Admin) y ejecutar los casos de las secciones 8–12.  
7. **Concurrencia:** Incluir tests de carga/estrés para assign-driver y pagos con múltiples claves para cuantificar race conditions y doble asignación/doble pago.

---

## 14. Estructura del reporte — Checklist

- [x] Resumen ejecutivo  
- [x] Hallazgos críticos (H-01, H-02, H-03)  
- [x] Hallazgos medios (H-04 a H-07)  
- [x] Hallazgos menores (H-08 a H-10)  
- [x] Riesgos potenciales  
- [x] Validaciones correctas  
- [x] Recomendaciones sin cambio de código  
- [x] Descubrimiento de endpoints, roles y sensibles  
- [x] Diseño de pruebas por rol (Passenger, Admin, Driver)  
- [x] Diseño de pruebas de estados y transiciones inválidas  
- [x] Diseño de pruebas de integridad financiera  
- [x] Diseño de pruebas de seguridad (IDOR, rol, overposting, token)  
- [x] Consideraciones de concurrencia  

**Clasificación por severidad:** Crítico 3, Alto 2, Medio 2, Bajo 3.  
**Impacto:** Financiero 4, Seguridad 3, Datos 3, UX 1.
