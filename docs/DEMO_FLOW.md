# MOVIX — Demo E2E (10 min)

Demo reproducible de punta a punta: tenancy, pricing, trips, driver matching, payments (simulación) y outbox/ops.

**Requisitos:** API en marcha con `ASPNETCORE_ENVIRONMENT=Development`, `Payments:Mode=Simulation`. Sin Stripe real.

---

## Variables (definir al inicio)

```powershell
# PowerShell
$BASE_URL = "http://localhost:8080"
$TENANT_ID = ""   # Se rellena tras crear tenant
$ADMIN_TOKEN = "" # Se rellena tras login admin
$PASSENGER_TOKEN = ""
$DRIVER_TOKEN = ""
```

```bash
# Bash
export BASE_URL="http://localhost:8080"
export TENANT_ID=""   # Se rellena tras crear tenant
export ADMIN_TOKEN=""
export PASSENGER_TOKEN=""
export DRIVER_TOKEN=""
```

---

## 0. Arranque

- Levantar API: `docker-compose up -d` o `dotnet run --project src/Movix.Api` (puerto 8080 por defecto en Docker).
- Health: `GET $BASE_URL/health` → 200.
- **Simulation Mode:** En `appsettings.Development.json` o variables: `Payments:Mode=Simulation`. No configurar Stripe.

---

## 1) Tenancy

### 1.1 Login Admin (seed)

El seed (Development) crea un admin con `ADMIN_EMAIL` y `ADMIN_PASSWORD`.

```bash
# Bash
curl -s -X POST "$BASE_URL/api/v1/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@movix.local","password":"TuPasswordSeguro"}' | jq .
```

```powershell
# PowerShell
Invoke-RestMethod -Uri "$BASE_URL/api/v1/auth/login" -Method Post -ContentType "application/json" -Body '{"email":"admin@movix.local","password":"TuPasswordSeguro"}'
```

**Esperado:** 200, body con `accessToken`, `refreshToken`. Guardar `accessToken` en `ADMIN_TOKEN`.

### 1.2 Crear Tenant

```bash
# Bash (sustituir ADMIN_TOKEN)
curl -s -X POST "$BASE_URL/api/v1/admin/tenants" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"Demo City"}' | jq .
```

**Esperado:** 200, body con `id`, `name`. Guardar `id` en `TENANT_ID`.

### 1.3 Listar Tenants

```bash
curl -s -X GET "$BASE_URL/api/v1/admin/tenants" \
  -H "Authorization: Bearer $ADMIN_TOKEN" | jq .
```

### Probar TENANT_REQUIRED y TENANT_INVALID

- **TENANT_REQUIRED:** Llamar a `GET /api/v1/fare/quote?distanceKm=5&durationMin=10` **sin** header `X-Tenant-Id` (con token válido). Esperado: 400, `code: "TENANT_REQUIRED"`.
- **TENANT_INVALID:** Enviar `X-Tenant-Id: not-a-guid`. Esperado: 400, `code: "TENANT_INVALID"`.

---

## 2) Pricing Engine

Todos los endpoints de tarifas requieren `X-Tenant-Id: $TENANT_ID` y rol Admin.

### 2.1 Crear TariffPlan

```bash
curl -s -X POST "$BASE_URL/api/v1/admin/tariffs" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID" \
  -H "Content-Type: application/json" \
  -d '{"name":"Standard","currency":"USD","baseFare":2.50,"pricePerKm":1.20,"pricePerMinute":0.25,"minimumFare":5.00}' | jq .
```

**Esperado:** 200, body con `id`, `tenantId`, `name`, `isActive`, etc. Guardar `id` como `TARIFF_ID`.

### 2.2 Activar TariffPlan

```bash
curl -s -X POST "$BASE_URL/api/v1/admin/tariffs/$TARIFF_ID/activate" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID" | jq .
```

**Esperado:** 200, mismo tariff con `isActive: true`.

### 2.3 Cotizar tarifa (Fare Quote)

```bash
curl -s -X GET "$BASE_URL/api/v1/fare/quote?distanceKm=5&durationMin=10" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID" | jq .
```

**Esperado:** 200, `{ "fareAmount": ..., "currency": "USD", "tariffPlanId": "..." }`.

Alternativa admin: `GET /api/v1/admin/tariffs/fare/quote?distanceKm=5&durationMin=10` (mismo headers).

---

## 3) Users / Drivers / Trips

### 3.1 Registro Passenger

```bash
curl -s -X POST "$BASE_URL/api/v1/auth/register" \
  -H "Content-Type: application/json" \
  -d '{"email":"passenger@demo.local","password":"Pass1234"}' -w "\nHTTP %{http_code}\n"
```

**Esperado:** 202 Accepted (sin body). Luego login para obtener token.

### 3.2 Login Passenger

```bash
curl -s -X POST "$BASE_URL/api/v1/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"email":"passenger@demo.local","password":"Pass1234"}' | jq .
```

Guardar `accessToken` en `PASSENGER_TOKEN`.

### 3.3 Driver: Onboarding

El conductor debe estar autenticado (usuario con rol Driver). Si el seed creó un driver, usar ese usuario; si no, registrar uno y asignar rol Driver en BD o usar un usuario ya Driver.

```bash
curl -s -X POST "$BASE_URL/api/v1/drivers/onboarding" \
  -H "Authorization: Bearer $DRIVER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"licenseNumber":"DL-001","vehiclePlate":"ABC-123","vehicleModel":"Sedan","vehicleColor":"White"}' | jq .
```

**Esperado:** 200, `{ "driverId": "...", "isVerified": false }`.

### 3.4 Driver Status Online

```bash
curl -s -X POST "$BASE_URL/api/v1/drivers/status" \
  -H "Authorization: Bearer $DRIVER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"status":1}' | jq .
```

**status:** 0=Offline, 1=Online, 2=Busy, 3=OnTrip. Usar `1` para Online.

### 3.5 Create Trip (Passenger)

Requiere `Idempotency-Key` y `X-Tenant-Id`.

```bash
curl -s -X POST "$BASE_URL/api/v1/trips" \
  -H "Authorization: Bearer $PASSENGER_TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID" \
  -H "Idempotency-Key: demo-trip-$(date +%s)" \
  -H "Content-Type: application/json" \
  -d '{"pickupLatitude":9.0,"pickupLongitude":-79.5,"dropoffLatitude":9.05,"dropoffLongitude":-79.55,"pickupAddress":"Origin","dropoffAddress":"Dest","estimatedAmount":10.50,"currency":"USD"}' | jq .
```

**Esperado:** 200, `{ "id": "...", "status": "Requested", ... }`. Guardar `id` como `TRIP_ID`.

### 3.6 Assign Driver (matching MVP)

Cualquier usuario autenticado puede llamar (en producción suele ser backend o Admin). Requiere al menos un driver Online y libre.

```bash
curl -s -X POST "$BASE_URL/api/v1/trips/$TRIP_ID/assign-driver" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID" | jq .
```

**Esperado:** 200, trip con `status: "Accepted"` y `driverId` poblado. 409 si no hay conductores disponibles.

### 3.7 Transiciones: Arrive → Start → Complete (Driver)

El conductor (o Admin) ejecuta las transiciones. **Complete** debe enviar `distanceKm` y `durationMinutes` para que se calcule y persista el fare snapshot.

```bash
# Arrive
curl -s -X POST "$BASE_URL/api/v1/trips/$TRIP_ID/arrive" \
  -H "Authorization: Bearer $DRIVER_TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID" | jq .

# Start
curl -s -X POST "$BASE_URL/api/v1/trips/$TRIP_ID/start" \
  -H "Authorization: Bearer $DRIVER_TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID" | jq .

# Complete (con snapshot de tarifa)
curl -s -X POST "$BASE_URL/api/v1/trips/$TRIP_ID/complete" \
  -H "Authorization: Bearer $DRIVER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"distanceKm":5.2,"durationMinutes":12,"tenantId":null}' | jq .
```

**Esperado:** 200 en cada uno. En Complete se usa la tarifa activa del tenant del trip; el trip queda con `finalAmount` y snapshot (TariffPlanIdUsed, BaseFareUsed, etc.) en BD.

### 3.8 Ver Trip (fare snapshot)

```bash
curl -s -X GET "$BASE_URL/api/v1/trips/$TRIP_ID" \
  -H "Authorization: Bearer $PASSENGER_TOKEN" | jq .
```

**Esperado:** 200, trip con `status: "Completed"`, `finalAmount` rellenado. Los campos de snapshot (TariffPlanIdUsed, BaseFareUsed, etc.) se persisten en la entidad Trip; el DTO expone `finalAmount` y `currency`.

---

## 4) Payments (Simulation Mode)

### 4.1 Create Payment

Requiere trip en estado Completed, usuario autenticado (típicamente el pasajero) e `Idempotency-Key`.

```bash
curl -s -X POST "$BASE_URL/api/v1/payments" \
  -H "Authorization: Bearer $PASSENGER_TOKEN" \
  -H "Idempotency-Key: demo-pay-$(date +%s)" \
  -H "Content-Type: application/json" \
  -d "{\"tripId\":\"$TRIP_ID\",\"amount\":10.50,\"currency\":\"USD\"}" | jq .
```

**Esperado:** 200, body con `id`, `tripId`, `status: "Pending"`, `externalPaymentId` (prefijo `SIM_`), `clientSecret` (prefijo `SIM_SECRET_`). Guardar `id` como `PAYMENT_ID`.

### 4.2 Simulate Webhook (solo Development)

Simula `payment_intent.succeeded` para marcar el pago como Completed.

```bash
curl -s -X POST "$BASE_URL/api/v1/payments/simulate-webhook" \
  -H "Content-Type: application/json" \
  -d "{\"paymentId\":\"$PAYMENT_ID\",\"eventType\":\"payment_intent.succeeded\"}" | jq .
```

**Esperado:** 200. El pago pasa a Completed. En Production este endpoint no existe (404).

### Protección contra downgrade (documentación)

- Si el pago ya está **Completed**, un evento `payment_intent.payment_failed` no cambia el estado (se rechaza el downgrade).
- Si está **Failed**, un `payment_intent.succeeded` no lo marca Completed.
- Si está **Refunded**, no se aceptan más cambios. Ver lógica en `ProcessStripeWebhookCommandHandler`.

---

## 5) Outbox + Ops

Todos los endpoints de ops requieren rol Admin (o Support).

### 5.1 GET Pagos (filtros)

```bash
curl -s -X GET "$BASE_URL/api/v1/admin/ops/payments?status=Completed" \
  -H "Authorization: Bearer $ADMIN_TOKEN" | jq .
```

Query params opcionales: `tenantId`, `status` (Pending|Completed|Failed|Refunded), `from`, `to`, `tripId`.

### 5.2 GET Outbox (deadletters)

```bash
curl -s -X GET "$BASE_URL/api/v1/admin/ops/outbox?deadletter=true" \
  -H "Authorization: Bearer $ADMIN_TOKEN" | jq .
```

Opcionales: `processed`, `deadletter`, `from`, `to`, `type`.

### 5.3 Reprocess DeadLetter

Si hay un mensaje en deadletter, se puede reprocesar (vuelve a cola para el OutboxProcessor).

```bash
curl -s -X POST "$BASE_URL/api/v1/admin/ops/outbox/$OUTBOX_MESSAGE_ID/reprocess" \
  -H "Authorization: Bearer $ADMIN_TOKEN" -w "\nHTTP %{http_code}\n"
```

**Esperado:** 204 No Content. 404 si no existe; 400 si el mensaje no está en estado deadletter.

---

## Minimal Demo (5 min)

1. Login Admin → `ADMIN_TOKEN`
2. Create Tenant → `TENANT_ID`
3. Create Tariff + Activate (con `X-Tenant-Id`)
4. Quote fare (con `X-Tenant-Id`)
5. Register + Login Passenger → `PASSENGER_TOKEN`
6. (Opcional) Login Driver o usar seed → `DRIVER_TOKEN`; onboarding + status Online
7. Create Trip (Passenger, `X-Tenant-Id`, Idempotency-Key) → `TRIP_ID`
8. Assign Driver → trip Accepted
9. Arrive / Start / Complete (Driver; en Complete enviar distanceKm y durationMinutes)
10. Create Payment (Passenger, Idempotency-Key) → `PAYMENT_ID`, comprobar `clientSecret` SIM_
11. Simulate webhook `payment_intent.succeeded`
12. GET admin/ops/payments?status=Completed

---

## Full Demo (10–12 min)

Incluye todo lo anterior más:

- Probar 400 TENANT_REQUIRED (fare/quote sin X-Tenant-Id).
- Probar 400 TENANT_INVALID (X-Tenant-Id inválido).
- GET admin/ops/outbox?deadletter=true.
- Si hay deadletter, POST admin/ops/outbox/{id}/reprocess y verificar 204.
- GET trip tras complete para ver `finalAmount` (fare snapshot).

---

## Troubleshooting

| Situación | Código / Respuesta | Acción |
|-----------|--------------------|--------|
| Falta `X-Tenant-Id` en fare/tariffs/trips (cuando aplica) | 400, `TENANT_REQUIRED` | Añadir header `X-Tenant-Id: <Guid>` |
| `X-Tenant-Id` no es un Guid válido | 400, `TENANT_INVALID` | Enviar Guid válido del tenant |
| Sin token o token inválido | 401 | Login y usar `Authorization: Bearer <token>` |
| Rol insuficiente (ej. Passenger en admin) | 403 | Usar usuario Admin/Support o Driver según endpoint |
| Payments sin Simulation y sin Stripe configurado | 503, `PAYMENT_GATEWAY_ERROR` | Usar `Payments:Mode=Simulation` o configurar Stripe |
| simulate-webhook en no-Development | 404 | Solo disponible con `ASPNETCORE_ENVIRONMENT=Development` |
| Idempotency-Key faltante en POST trips/payments | 400, `IDEMPOTENCY_KEY_REQUIRED` | Añadir header `Idempotency-Key: <valor>` |
| Assign driver sin conductores online | 409, `NO_DRIVERS_AVAILABLE` | Poner al menos un driver Online (drivers/status) |

---

## Demo Checklist

- [ ] API arrancada (health 200), Payments:Mode=Simulation
- [ ] Login Admin y crear tenant; guardar TENANT_ID
- [ ] Probar TENANT_REQUIRED / TENANT_INVALID (fare/quote o tariffs sin header / header inválido)
- [ ] Crear y activar TariffPlan; cotizar tarifa con X-Tenant-Id
- [ ] Registro Passenger y login; guardar PASSENGER_TOKEN
- [ ] Driver: onboarding + status Online; guardar DRIVER_TOKEN
- [ ] Create Trip (Passenger, X-Tenant-Id, Idempotency-Key)
- [ ] Assign Driver; comprobar status Accepted
- [ ] Arrive → Start → Complete (con distanceKm y durationMinutes)
- [ ] GET trip y comprobar finalAmount (fare snapshot)
- [ ] Create Payment (Idempotency-Key); comprobar externalPaymentId SIM_ y clientSecret SIM_SECRET_
- [ ] POST simulate-webhook payment_intent.succeeded; comprobar pago Completed
- [ ] GET admin/ops/payments?status=Completed
- [ ] GET admin/ops/outbox (opcional deadletter=true)
- [ ] Reprocess deadletter si existe (POST admin/ops/outbox/{id}/reprocess) → 204

---

## Knobs de configuración (demo)

| Sección | Clave | Descripción | Demo |
|--------|--------|-------------|------|
| Payments | `Payments:Mode` | `Simulation` = sin Stripe real; `Stripe` = pasarela real | `Simulation` (requerido para demo sin Stripe) |
| Outbox | `Outbox:Enabled` | Activa el worker que procesa la outbox | `true` en Development (appsettings.Development.json) |
| Outbox | `Outbox:PollIntervalMs` | Intervalo entre ciclos del worker (ms) | 500 |
| Outbox | `Outbox:MaxBatchSize` | Mensajes por ciclo (FIFO) | 50 |
| Outbox | `Outbox:ErrorBackoffMs` | Backoff tras error inesperado (+ jitter) | 2000 |
| Seed | `ADMIN_EMAIL`, `ADMIN_PASSWORD` | Usuario Admin inicial (Development) | Requeridos para login Admin en demo |
| Seed | `DRIVER_EMAIL`, `DRIVER_PASSWORD` | Conductor opcional (Development) | Opcional para flujo driver (onboarding, arrive/start/complete) |

En Tests el worker no debe correr automáticamente: no configurar `Outbox:Enabled` en el proyecto de tests o usar `false`.

---

## Cómo correr el demo

1. **Entorno:** `ASPNETCORE_ENVIRONMENT=Development`, `Payments:Mode=Simulation`. Opcional: `ADMIN_EMAIL`, `ADMIN_PASSWORD`, `DRIVER_EMAIL`, `DRIVER_PASSWORD` para seed.
2. **Levantar API:** desde raíz del repo, `docker-compose up -d` (API en http://localhost:8080) o `dotnet run --project src/Movix.Api`.
3. **Variables:** definir `BASE_URL`, luego ir obteniendo `ADMIN_TOKEN`, `TENANT_ID`, `PASSENGER_TOKEN`, `DRIVER_TOKEN`, `TRIP_ID`, `PAYMENT_ID` según los pasos anteriores.
4. **Ejecutar** los bloques de curl (bash) o Invoke-RestMethod (PowerShell) en el orden de las secciones 1 → 2 → 3 → 4 → 5.
5. **Script opcional:** `docs/demo/demo.ps1` (PowerShell) automatiza el flujo; imprime IDs y tokens. Requiere API en marcha y variables de seed configuradas.
