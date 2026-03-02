# AuthController

Base route: `api/v1/auth`  
Controller: No [Authorize]; endpoints are public except where noted.  
Rate limiting: `auth` policy on register, login, refresh.

---

## Endpoint: POST api/v1/auth/register

### Roles permitidos:
Public.

### RequireTenant:
No

### Request:
- Body: RegisterRequest — Email (string), Password (string), TenantId (Guid)

### Response (202):
- Accepted, no body (email enumeration prevention).

### Posibles errores:
- TENANT_NOT_FOUND: 400
- TENANT_INACTIVE: 400
- (other validation): 400

---

## Endpoint: POST api/v1/auth/login

### Roles permitidos:
Public.

### RequireTenant:
No

### Request:
- Body: LoginRequest — Email (string), Password (string)

### Response (200):
- LoginResponse: AccessToken (string), RefreshToken (string), AccessTokenExpiresAtUtc (DateTime), ExpiresInSeconds (int)

### Posibles errores:
- INVALID_CREDENTIALS: 401

---

## Endpoint: POST api/v1/auth/refresh

### Roles permitidos:
Public.

### RequireTenant:
No

### Request:
- Body: RefreshRequest — RefreshToken (string)

### Response (200):
- LoginResponse (same as login).

### Posibles errores:
- REFRESH_TOKEN_INVALID: 401
- REFRESH_TOKEN_REUSE: 401

---

## Endpoint: POST api/v1/auth/logout

### Roles permitidos:
Public (typically called with valid token).

### RequireTenant:
No

### Request:
- Body: LogoutRequest — RefreshToken (string, nullable)

### Response (200):
- Empty body. No error mapping; always 200.
