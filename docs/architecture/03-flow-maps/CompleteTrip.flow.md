# Flow: CompleteTrip

**Layer 1: HTTP Layer**  
POST `/api/v1/trips/{id}/complete`. Body optional: CompleteTripRequest (DistanceKm, DurationMinutes). No RequireTenant on this action.

**Layer 2: Controller**  
TripsController.Complete. [Authorize(Roles = "Driver,Admin")]. Sends TransitionTripCommand(id, TripStatus.Completed, null, request?.DistanceKm, request?.DurationMinutes). Uses ToResult(result); MapError on failure, Ok(result.Data) on success.

**Layer 3: Command/Query**  
TransitionTripCommand(TripId, TargetStatus = Completed, Reason = null, DistanceKm, DurationMinutes). IRequest<Result<TripDto>>.

**Layer 4: Handler**  
TransitionTripCommandHandler. Load trip with driver (GetByIdWithDriverAsync); null -> TRIP_NOT_FOUND. If trip.TenantId null, set from TenantContext; still null -> TENANT_REQUIRED. TripStateMachine.CanTransition(trip.Status, Completed) -> else INVALID_TRANSITION. ABAC: for Completed, driver must be assigned; if not Admin/Support, userId must equal trip.DriverId -> else FORBIDDEN. Set trip.Status = Completed, UpdatedAtUtc, UpdatedBy; append TripStatusHistory. If DistanceKm and DurationMinutes provided: get applicable tariff via ITariffPlanRepository.GetApplicableTariffAsync(tenantId, now); if plan not null, compute fare with IFareCalculator.Calculate, set trip FinalAmount, Currency, DistanceKm, DurationMinutes, TariffPlanIdUsed, BaseFareUsed, PricePerKmUsed, PricePerMinuteUsed, MinimumFareUsed. If Completed or Cancelled and DriverId set: clear driver availability CurrentTripId. If Completed: add OutboxMessage Type "TripCompleted" with payload (tripId, driverId, tenantId, occurredAtUtc). SaveChangesAsync; ConcurrencyException -> CONFLICT. Return Map(trip) TripDto.

**Layer 5: Domain Rules**  
Transition to Completed allowed by state machine. Optional final fare snapshot from applicable tariff at completion time. Driver availability released.

**Layer 6: Tenant Enforcement**  
Trip.TenantId used for tariff lookup (tenantId from trip). No RequireTenant on action; handler uses trip's tenant for GetApplicableTariffAsync. Access control: driver or Admin/Support only.

**Layer 7: Repository/DB**  
ITripRepository.GetByIdWithDriverAsync. ITariffPlanRepository.GetApplicableTariffAsync(tenantId, now). IDriverAvailabilityRepository (get by driver, clear CurrentTripId). IUnitOfWork.SaveChangesAsync. Writes: trips (Status, FinalAmount, Currency, DistanceKm, DurationMinutes, tariff snapshot fields, UpdatedAtUtc, UpdatedBy), trip_status_histories (insert), driver_availabilities (CurrentTripId = null).

**Layer 8: Outbox**  
IOutboxMessageRepository.AddAsync(OutboxMessage Type = "TripCompleted", Payload). Same SaveChangesAsync as above.

**Layer 9: Audit**  
Not used in CompleteTrip (TransitionTripCommandHandler does not call IAuditService for Completed).

**Layer 10: Response mapping**  
200 with TripDto. Errors via MapError: FORBIDDEN 403, TRIP_NOT_FOUND 404, TRIP_INVALID_STATE 400, NO_DRIVERS_AVAILABLE 409, INVALID_TRANSITION 422, DRIVER_NOT_ASSIGNED 422, CONFLICT 409, default 400.
