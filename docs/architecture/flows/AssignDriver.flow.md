# Flow: AssignDriver

1. **HTTP**  
   POST `/api/v1/trips/{id}/assign-driver`. Route parameter `id` is the trip ID. No body.

2. **Controller**  
   - `[Authorize(Roles = "Admin,Support")]` and `[RequireTenant]` on action.  
   - RequireTenant ensures `X-Tenant-Id` (or JWT tenant_id) is present and valid; otherwise 400 TENANT_REQUIRED or TENANT_INVALID.  
   - Sends `AssignDriverCommand(id)` (single argument: TripId = route id).

3. **Handler (AssignDriverCommandHandler)**  
   - **Role check:** If current user role is not Admin, Support or SuperAdmin, return FORBIDDEN.  
   - **Trip load:** `ITripRepository.GetByIdAsync(request.TripId)`. If null, TRIP_NOT_FOUND.  
   - **State check:** Trip must be in status Requested; otherwise TRIP_INVALID_STATE.  
   - **Tenant check:** If not SuperAdmin and trip.TenantId != TenantContext.TenantId, return FORBIDDEN.  
   - **Availability:** `IDriverAvailabilityRepository.GetFirstAvailableAsync()`. If null, NO_DRIVERS_AVAILABLE.  
   - **Driver tenant check:** If not SuperAdmin and driver's TenantId != trip.TenantId, return FORBIDDEN.  
   - **Updates:** Set trip.DriverId = availability.DriverId, trip.Status = Accepted, trip.UpdatedAtUtc/UpdatedBy; append TripStatusHistory; set availability.CurrentTripId = trip.Id, availability.UpdatedAtUtc.  
   - **Outbox:** Add OutboxMessage Type = "DriverAssigned" with payload (tripId, driverId, occurredAtUtc).  
   - **Persistence:** `IUnitOfWork.SaveChangesAsync()`. On ConcurrencyException return NO_DRIVERS_AVAILABLE.  
   - **Audit:** `IAuditService.LogAsync("AssignDriver", "Trip", trip.Id, new { driverId = availability.DriverId })`.  
   - Return `Result.Success(Map(trip))` (TripDto).

4. **DB impact**  
   - `trips`: DriverId, Status, UpdatedAtUtc, UpdatedBy; new row in trip_status_histories.  
   - `driver_availabilities`: CurrentTripId, UpdatedAtUtc for the selected availability.  
   - `outbox_messages`: one new row.  
   - `audit_logs`: one new row (from AuditService).

5. **Response**  
   - 200 with TripDto.  
   - Errors mapped by TripsController.MapError: FORBIDDEN 403, TRIP_NOT_FOUND 404, TRIP_INVALID_STATE 400, NO_DRIVERS_AVAILABLE 409, others 400.
