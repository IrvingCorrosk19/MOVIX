# Flow: AssignDriver

**Layer 1: HTTP Layer**  
POST `/api/v1/trips/{id}/assign-driver`. Route parameter id = trip ID. No body. Rate limiting: trips policy.

**Layer 2: Controller**  
TripsController.AssignDriver. [Authorize(Roles = "Admin,Support")], [RequireTenant]. Sends AssignDriverCommand(id). On failure uses MapError (FORBIDDEN 403, TRIP_NOT_FOUND 404, TRIP_INVALID_STATE 400, NO_DRIVERS_AVAILABLE 409, INVALID_TRANSITION 422, DRIVER_NOT_ASSIGNED 422, CONFLICT 409, default 400). On success returns Ok(result.Data).

**Layer 3: Command/Query**  
AssignDriverCommand(TripId). IRequest<Result<TripDto>>.

**Layer 4: Handler**  
AssignDriverCommandHandler. Role check: not Admin/Support/SuperAdmin -> FORBIDDEN. Load trip by request.TripId; null -> TRIP_NOT_FOUND. Trip status must be Requested -> TRIP_INVALID_STATE. Tenant check: if not SuperAdmin and trip.TenantId != TenantContext.TenantId -> FORBIDDEN. Get first available driver via IDriverAvailabilityRepository.GetFirstAvailableAsync(); null -> NO_DRIVERS_AVAILABLE. Driver tenant check: if not SuperAdmin and driver.TenantId != trip.TenantId -> FORBIDDEN. Update trip: DriverId = availability.DriverId, Status = Accepted, UpdatedAtUtc, UpdatedBy. Append TripStatusHistory (Requested -> Accepted). Update availability: CurrentTripId = trip.Id, UpdatedAtUtc. Add OutboxMessage Type DriverAssigned. SaveChangesAsync; on ConcurrencyException return NO_DRIVERS_AVAILABLE. Call IAuditService.LogAsync("AssignDriver", "Trip", trip.Id, { driverId }). Return Map(trip) as TripDto.

**Layer 5: Domain Rules**  
Transition Requested -> Accepted only. Driver must be from availability slot; trip gains DriverId and status Accepted. Availability record linked to trip.

**Layer 6: Tenant Enforcement**  
RequireTenant ensures tenant in context. Handler enforces trip.TenantId == context TenantId (unless SuperAdmin) and driver.TenantId == trip.TenantId.

**Layer 7: Repository/DB**  
ITripRepository.GetByIdAsync. IDriverAvailabilityRepository.GetFirstAvailableAsync. In-memory updates on trip and availability; IUnitOfWork.SaveChangesAsync. Writes: trips (DriverId, Status, UpdatedAtUtc, UpdatedBy), trip_status_histories (insert), driver_availabilities (CurrentTripId, UpdatedAtUtc).

**Layer 8: Outbox**  
IOutboxMessageRepository.AddAsync(OutboxMessage Type = "DriverAssigned", Payload with tripId, driverId, occurredAtUtc). Same transaction as SaveChangesAsync.

**Layer 9: Audit**  
IAuditService.LogAsync("AssignDriver", "Trip", trip.Id, new { driverId = availability.DriverId }). Called after SaveChangesAsync. Writes to audit_logs (separate SaveChanges in AuditService).

**Layer 10: Response mapping**  
200 with TripDto. Errors mapped as in Controller (403, 404, 400, 409, 422).
