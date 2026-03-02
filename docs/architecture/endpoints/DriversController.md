# DriversController

Base route: `api/v1/drivers`  
Controller: `[Authorize(Roles = "Driver,Admin")]`. No RequireTenant at class level.

---

## Endpoint: POST api/v1/drivers/onboarding

### Roles permitidos:
Driver, Admin

### RequireTenant:
No

### Request:
- Body: DriverOnboardingRequest — LicenseNumber (string?), VehiclePlate (string?), VehicleModel (string?), VehicleColor (string?)

### Response (200):
- DriverOnboardingResponse (handler result type).

### Posibles errores:
- (validation, e.g. already onboarded): 400

---

## Endpoint: POST api/v1/drivers/status

### Roles permitidos:
Driver, Admin

### RequireTenant:
No

### Request:
- Body: DriverStatusRequest — Status (DriverStatus enum)

### Response (200):
- Empty body.

### Posibles errores:
- (handler): 400

---

## Endpoint: POST api/v1/drivers/location

### Roles permitidos:
Driver, Admin

### RequireTenant:
No

### Request:
- Body: DriverLocationRequest — Latitude (double), Longitude (double), Heading (double?), Speed (double?)

### Response (200):
- Empty body.

### Posibles errores:
- (handler): 400
