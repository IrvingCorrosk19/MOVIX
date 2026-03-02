# Flow: CreateTrip

**Layer 1: HTTP Layer**  
POST `/api/v1/trips`. Header `Idempotency-Key` required. Body: CreateTripRequest (PickupLatitude, PickupLongitude, DropoffLatitude, DropoffLongitude, PickupAddress, DropoffAddress, EstimatedAmount, Currency). Rate limiting: trips policy.

**Layer 2: Controller**  
TripsController.Create. [Authorize], [RequireTenant]. If Idempotency-Key missing returns 400 IDEMPOTENCY_KEY_REQUIRED. Builds CreateTripCommand(idempotencyKey, pickup/dropoff coords and addresses, estimatedAmount, currency). Sends via MediatR. On failure returns BadRequest with error/code; on success returns Ok(result.Data).

**Layer 3: Command/Query**  
CreateTripCommand: IdempotencyKey, PickupLatitude, PickupLongitude, DropoffLatitude, DropoffLongitude, PickupAddress, DropoffAddress, EstimatedAmount, Currency. IRequest<Result<TripDto>>.

**Layer 4: Handler**  
CreateTripCommandHandler. Validates UserId (null -> UNAUTHORIZED), TenantId from ITenantContext (null -> TENANT_REQUIRED). Checks idempotency store; if key already exists and trip exists, returns cached TripDto (success). Builds Trip entity with IdempotencyKey, PassengerId (userId), TenantId (from context), Status Requested, PickupLocation/DropoffLocation (geometry), addresses, EstimatedAmount, Currency. Adds trip via ITripRepository.AddAsync. SaveChangesAsync via IUnitOfWork. Stores idempotency key -> trip.Id in IIdempotencyService. Returns Map(trip) as TripDto.

**Layer 5: Domain Rules**  
Trip created in Requested state. PassengerId and TenantId set from current user and tenant context. No state machine transition on create.

**Layer 6: Tenant Enforcement**  
RequireTenant on action ensures ITenantContext.TenantId is set before handler runs. Handler uses that TenantId for trip.TenantId. No cross-tenant check on create (tenant is the caller's).

**Layer 7: Repository/DB**  
ITripRepository.AddAsync(trip). IUnitOfWork.SaveChangesAsync(). IIdempotencyService.GetResponseAsync / StoreAsync (Redis or equivalent). Tables: trips (insert), trip_status_histories not written on create in current flow.

**Layer 8: Outbox**  
Not used in CreateTrip.

**Layer 9: Audit**  
Not used in CreateTrip.

**Layer 10: Response mapping**  
Success: 200 OK with TripDto (Id, Status, PickupAddress, DropoffAddress, EstimatedAmount, FinalAmount, Currency, CreatedAtUtc). Errors: 400 with error and code (IDEMPOTENCY_KEY_REQUIRED, UNAUTHORIZED, TENANT_REQUIRED, or other handler failure).
