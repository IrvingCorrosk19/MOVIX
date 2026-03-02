# Flow: AcceptTrip

**Layer 1: HTTP Layer**  
POST `/api/v1/trips/{id}/accept`. Body: AcceptTripRequest (VehicleId: Guid). Rate limiting: trips policy.

**Layer 2: Controller**  
TripsController.Accept. [Authorize(Roles = "Driver,Admin")]. No RequireTenant. Sends AcceptTripCommand(id, request.VehicleId). Uses ToResult(result) -> MapError on failure, Ok(result.Data) on success.

**Layer 3: Command/Query**  
AcceptTripCommand(TripId, VehicleId). IRequest<Result<TripDto>>.

**Layer 4: Handler**  
AcceptTripCommandHandler. UserId null -> UNAUTHORIZED. Load trip by request.TripId; null -> TRIP_NOT_FOUND. TripStateMachine.CanTransition(trip.Status, Accepted) must be true -> else INVALID_TRANSITION. Load driver by userId (GetByUserIdWithVehiclesAsync); null -> DRIVER_NOT_FOUND. Tenant: trip.TenantId null or != driver.TenantId -> FORBIDDEN. Vehicle: driver.Vehicles must contain request.VehicleId -> else VEHICLE_NOT_FOUND. Update trip: DriverId = driver.Id, VehicleId = vehicle.Id, Status = Accepted, UpdatedAtUtc, UpdatedBy. Append TripStatusHistory. SaveChangesAsync; ConcurrencyException -> CONFLICT. IAuditService.LogAsync("AcceptTrip", "Trip", trip.Id, null). Return TripDto.

**Layer 5: Domain Rules**  
Transition to Accepted allowed by state machine. Driver and vehicle must belong to same tenant as trip. Current user must be the driver.

**Layer 6: Tenant Enforcement**  
No RequireTenant on action. Handler enforces trip.TenantId == driver.TenantId; if not, FORBIDDEN. No context tenant comparison for this path (driver is current user).

**Layer 7: Repository/DB**  
ITripRepository.GetByIdAsync. IDriverRepository.GetByUserIdWithVehiclesAsync. Updates: trips (DriverId, VehicleId, Status, UpdatedAtUtc, UpdatedBy), trip_status_histories (insert). IUnitOfWork.SaveChangesAsync.

**Layer 8: Outbox**  
Not used in AcceptTrip.

**Layer 9: Audit**  
IAuditService.LogAsync("AcceptTrip", "Trip", trip.Id, null). Called after SaveChangesAsync.

**Layer 10: Response mapping**  
200 with TripDto. MapError: FORBIDDEN 403, TRIP_NOT_FOUND 404, INVALID_TRANSITION 422, CONFLICT 409, others 400 (UNAUTHORIZED, DRIVER_NOT_FOUND, VEHICLE_NOT_FOUND).
