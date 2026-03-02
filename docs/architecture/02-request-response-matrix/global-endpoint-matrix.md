# Global Endpoint Matrix

Data extracted from existing endpoint documentation. One row per HTTP endpoint.

| Endpoint | Method | Roles | RequireTenant | Idempotency | Possible Errors | Calls Handler | Uses Audit | Uses Outbox |
|----------|--------|-------|---------------|-------------|-----------------|---------------|------------|-------------|
| api/v1/auth/register | POST | Public | No | No | TENANT_NOT_FOUND, TENANT_INACTIVE, 400 | RegisterCommandHandler | No | No |
| api/v1/auth/login | POST | Public | No | No | INVALID_CREDENTIALS 401 | LoginCommandHandler | No | No |
| api/v1/auth/refresh | POST | Public | No | No | REFRESH_TOKEN_INVALID, REFRESH_TOKEN_REUSE 401 | RefreshTokenCommandHandler | No | No |
| api/v1/auth/logout | POST | Public | No | No | (none; always 200) | LogoutCommandHandler | No | No |
| api/v1/trips | POST | Any authenticated | Yes | Yes (header) | IDEMPOTENCY_KEY_REQUIRED, UNAUTHORIZED, TENANT_REQUIRED, 400 | CreateTripCommandHandler | No | No |
| api/v1/trips/{id} | GET | Any; handler: owner or Admin/Support/SuperAdmin | No | No | TRIP_NOT_FOUND 404, FORBIDDEN 403 | GetTripQueryHandler | No | No |
| api/v1/trips/{id}/assign-driver | POST | Admin, Support | Yes | No | FORBIDDEN 403, TRIP_NOT_FOUND 404, TRIP_INVALID_STATE 400, NO_DRIVERS_AVAILABLE 409, 400 | AssignDriverCommandHandler | Yes | Yes |
| api/v1/trips/{id}/accept | POST | Driver, Admin | No | No | UNAUTHORIZED, TRIP_NOT_FOUND 404, INVALID_TRANSITION 422, DRIVER_NOT_FOUND, FORBIDDEN 403, VEHICLE_NOT_FOUND, CONFLICT 409, 400 | AcceptTripCommandHandler | Yes | No |
| api/v1/trips/{id}/arrive | POST | Driver, Admin | Yes | No | FORBIDDEN 403, TRIP_NOT_FOUND 404, TRIP_INVALID_STATE 400, NO_DRIVERS_AVAILABLE 409, INVALID_TRANSITION 422, DRIVER_NOT_ASSIGNED 422, CONFLICT 409, 400 | TransitionTripCommandHandler | No | No |
| api/v1/trips/{id}/start | POST | Driver, Admin | Yes | No | (same MapError set) | TransitionTripCommandHandler | No | No |
| api/v1/trips/{id}/complete | POST | Driver, Admin | No | No | (same MapError set) | TransitionTripCommandHandler | No | Yes (TripCompleted) |
| api/v1/trips/{id}/cancel | POST | Any; handler: passenger/driver/Admin/Support | Yes | No | (same MapError set) | TransitionTripCommandHandler | No | No |
| api/v1/payments | POST | Any authenticated | No | Yes (header) | IDEMPOTENCY_KEY_REQUIRED, PAYMENT_GATEWAY_ERROR 503, FORBIDDEN 403, UNAUTHORIZED, TRIP_NOT_FOUND, TRIP_NOT_COMPLETED, UNAUTHORIZED_PAYMENT, 400 | CreatePaymentCommandHandler | Yes | Yes |
| api/v1/payments/webhook | POST | AllowAnonymous | No | By event id | WEBHOOK_SIGNATURE_INVALID, WEBHOOK_INVALID 400 | ProcessStripeWebhookCommandHandler | No | (internal) |
| api/v1/payments/simulate-webhook | POST | AllowAnonymous | No | By event id | 404 if not Dev, INVALID_REQUEST, PAYMENT_NOT_FOUND 404, PAYMENT_NOT_SIMULATABLE 400 | ProcessStripeWebhookCommandHandler | No | (internal) |
| api/v1/admin/tariffs | POST | Admin, Support | Yes | No | 400 validation | CreateTariffPlanCommandHandler | No | No |
| api/v1/admin/tariffs/{id} | PUT | Admin, Support | Yes | No | TARIFF_NOT_FOUND 404, TARIFF_CROSS_TENANT 403, 400 | UpdateTariffPlanCommandHandler | No | No |
| api/v1/admin/tariffs/{id}/activate | POST | Admin, Support | Yes | No | TARIFF_NOT_FOUND 404, TARIFF_CROSS_TENANT 403, TARIFF_PRIORITY_CONFLICT 400 | ActivateTariffPlanCommandHandler | Yes | No |
| api/v1/admin/tariffs/active | GET | Admin, Support | Yes | No | NO_ACTIVE_TARIFF, NO_APPLICABLE_TARIFF 404, 400 | GetActiveTariffPlanQueryHandler | No | No |
| api/v1/admin/tariffs/fare/quote | GET | Admin, Support | Yes | No | NO_ACTIVE_TARIFF, NO_APPLICABLE_TARIFF 404, 400 | QuoteFareQueryHandler | No | No |
| api/v1/fare/quote | GET | Any authenticated | Yes | No | NO_ACTIVE_TARIFF, NO_APPLICABLE_TARIFF 404, 400 | QuoteFareQueryHandler | No | No |
| api/v1/admin/tenants | POST | Admin, Support | No | No | 400 | CreateTenantCommandHandler | No | No |
| api/v1/admin/tenants | GET | Admin, Support | No | No | 400 | GetTenantsQueryHandler | No | No |
| api/v1/admin/trips | GET | Admin, Support | No | No | TENANT_REQUIRED 400 | GetAdminTripsQueryHandler | No | No |
| api/v1/admin/drivers | GET | Admin, Support | No | No | TENANT_REQUIRED 400 | GetAdminDriversQueryHandler | No | No |
| api/v1/admin/outbox/{id}/reprocess | POST | Admin, Support | No | No | 404, 400 | ReprocessDeadLetterCommandHandler | No | No |
| api/v1/admin/ops/payments | GET | Admin, Support | No | No | (none) | (repository direct) | No | No |
| api/v1/admin/ops/outbox | GET | Admin, Support | No | No | (none) | (DbContext direct) | No | No |
| api/v1/admin/ops/outbox/{id}/reprocess | POST | Admin, Support | No | No | 404, 400 | ReprocessDeadLetterCommandHandler | No | No |
| api/v1/drivers/onboarding | POST | Driver, Admin | No | No | 400 | DriverOnboardingCommandHandler | No | No |
| api/v1/drivers/status | POST | Driver, Admin | No | No | 400 | DriverStatusCommandHandler | No | No |
| api/v1/drivers/location | POST | Driver, Admin | No | No | 400 | DriverLocationCommandHandler | No | No |
