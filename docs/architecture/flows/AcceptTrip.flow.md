# Flow: AcceptTrip

1. **HTTP**  
   POST `/api/v1/trips/{id}/accept`. Body: `AcceptTripRequest { VehicleId }`.

2. **Controller**  
   - `[Authorize(Roles = "Driver,Admin")]`. No RequireTenant on this action.  
   - Sends `AcceptTripCommand(id, request.VehicleId)`.  
   - Uses `ToResult(result)` which calls `MapError` on failure (same as other trip endpoints).

3. **Handler (AcceptTripCommandHandler)**  
   - **Auth:** If `ICurrentUserService.UserId` is null, return UNAUTHORIZED.  
   - **Trip:** `ITripRepository.GetByIdAsync(request.TripId)`. If null, TRIP_NOT_FOUND.  
   - **Transition:** `TripStateMachine.CanTransition(trip.Status, TripStatus.Accepted)` must be true; else INVALID_TRANSITION.  
   - **Driver:** `IDriverRepository.GetByUserIdWithVehiclesAsync(userId)`. If null, DRIVER_NOT_FOUND.  
   - **Tenant:** If trip.TenantId is null or != driver.TenantId, return FORBIDDEN.  
   - **Vehicle:** Driver's vehicles must contain request.VehicleId; else VEHICLE_NOT_FOUND.  
   - **Updates:** trip.DriverId = driver.Id, trip.VehicleId = vehicle.Id, trip.Status = Accepted, trip.UpdatedAtUtc/UpdatedBy; append TripStatusHistory.  
   - **Persistence:** `IUnitOfWork.SaveChangesAsync()`. On ConcurrencyException return CONFLICT.  
   - **Audit:** `IAuditService.LogAsync("AcceptTrip", "Trip", trip.Id, null)`.  
   - Return `Result.Success(TripDto)`.

4. **DB impact**  
   - `trips`: DriverId, VehicleId, Status, UpdatedAtUtc, UpdatedBy; one new trip_status_histories row.  
   - `audit_logs`: one new row.

5. **Response**  
   - 200 with TripDto.  
   - MapError: FORBIDDEN 403, TRIP_NOT_FOUND 404, INVALID_TRANSITION 422, CONFLICT 409, others 400 (UNAUTHORIZED, DRIVER_NOT_FOUND, VEHICLE_NOT_FOUND fall to default 400).
