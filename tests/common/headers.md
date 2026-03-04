# Headers comunes – Movix API

## Authorization

- **Uso:** En todos los endpoints que requieren autenticación (excepto `/auth/*`, `/payments/webhook`, `/payments/simulate-webhook`).
- **Formato:** `Authorization: Bearer <access_token>`
- **Origen del token:** Respuesta de `POST /api/v1/auth/login` → campo `accessToken`.
- **Renovación:** `POST /api/v1/auth/refresh` con `refreshToken` devuelve un nuevo par de tokens.

---

## X-Tenant-Id

- **Uso:** Obligatorio en endpoints marcados con `[RequireTenant]`: tarifas, crear viaje, asignar conductor, llegar/iniciar/cancelar viaje, cotizar tarifa, etc.
- **Formato:** `X-Tenant-Id: <guid>`
- **Ejemplo:** `X-Tenant-Id: 00000000-0000-0000-0000-000000000001`
- **Sin header o Guid inválido:** Respuesta `400` con `{ "error": "...", "code": "TENANT_REQUIRED" }` o `"TENANT_INVALID"`.

---

## Idempotency-Key

- **Uso:** Obligatorio en:
  - `POST /api/v1/trips` (crear viaje)
  - `POST /api/v1/payments` (crear pago)
- **Formato:** `Idempotency-Key: <string único por operación>`
- **Ejemplo:** `Idempotency-Key: trip-{{$guid}}` o `payment-{{$timestamp}}`
- **Propósito:** Evitar duplicados si el cliente reintenta; la API devuelve el mismo resultado para la misma clave.
- **Sin header:** Respuesta `400` con `{ "error": "Idempotency-Key header is required", "code": "IDEMPOTENCY_KEY_REQUIRED" }`.
