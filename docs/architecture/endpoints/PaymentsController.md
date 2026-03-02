# PaymentsController

Base route: `api/v1/payments`  
Controller: `[Authorize]` (except Webhook and SimulateWebhook).

---

## Endpoint: POST api/v1/payments

### Roles permitidos:
Any authenticated user.

### RequireTenant:
No (tenant enforced in handler via trip.TenantId vs ITenantContext).

### Request:
- Header: `Idempotency-Key` (required; missing returns 400 IDEMPOTENCY_KEY_REQUIRED).
- Body: CreatePaymentRequest — TripId (Guid), Amount (decimal), Currency (string, nullable, default USD)

### Response (200):
- PaymentDto: Id, TripId, Amount, Currency, Status, CreatedAtUtc, ClientSecret (nullable)

### Posibles errores:
- IDEMPOTENCY_KEY_REQUIRED: 400
- PAYMENT_GATEWAY_ERROR: 503
- FORBIDDEN: 403
- UNAUTHORIZED, TRIP_NOT_FOUND, TRIP_NOT_COMPLETED, UNAUTHORIZED_PAYMENT, (other): 400

---

## Endpoint: POST api/v1/payments/webhook

### Roles permitidos:
AllowAnonymous

### RequireTenant:
No

### Request:
- Body: raw JSON (Stripe webhook payload).
- Header: Stripe-Signature.

### Response (200):
- Empty body on success; idempotent by event id.

### Posibles errores:
- WEBHOOK_SIGNATURE_INVALID: 400
- WEBHOOK_INVALID: 400

---

## Endpoint: POST api/v1/payments/simulate-webhook

### Roles permitidos:
AllowAnonymous. Returns 404 if not Development.

### RequireTenant:
No

### Request:
- Body: SimulateWebhookRequest — PaymentId (Guid?), EventType (string?)

### Response (200):
- Empty body.

### Posibles errores:
- 404 if not Development
- INVALID_REQUEST: 400
- PAYMENT_NOT_FOUND: 404
- PAYMENT_NOT_SIMULATABLE: 400
