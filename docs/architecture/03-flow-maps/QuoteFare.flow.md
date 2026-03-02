# Flow: QuoteFare

**Layer 1: HTTP Layer**  
Two entry points: (1) GET `/api/v1/admin/tariffs/fare/quote?distanceKm=&durationMin=` (TariffsController, Admin/Support, RequireTenant). (2) GET `/api/v1/fare/quote?distanceKm=&durationMin=` (FareController, Authorize, RequireTenant). Both pass tenant from context and query params to same handler.

**Layer 2: Controller**  
TariffsController.QuoteFare or FareController.Quote. TenantId from ITenantContext.TenantId (required by RequireTenant). Sends QuoteFareQuery(tenantId, distanceKm, durationMin). On failure: NO_ACTIVE_TARIFF or NO_APPLICABLE_TARIFF -> 404; else 400. Success -> Ok(result.Data).

**Layer 3: Command/Query**  
QuoteFareQuery(TenantId, DistanceKm, DurationMinutes). IRequest<Result<QuoteFareDto>>.

**Layer 4: Handler**  
QuoteFareQueryHandler. atUtc = IDateTimeService.UtcNow. plan = ITariffPlanRepository.GetApplicableTariffAsync(tenantId, atUtc, ct). plan null -> NO_APPLICABLE_TARIFF. fare = IFareCalculator.Calculate(plan.BaseFare, plan.PricePerKm, plan.PricePerMinute, plan.MinimumFare, distanceKm, durationMinutes). Return Success(QuoteFareDto(fare, plan.Currency, plan.Id)).

**Layer 5: Domain Rules**  
Applicable plan: TenantId match, IsActive true, atUtc in [EffectiveFromUtc, EffectiveUntilUtc) (nulls mean unbounded). Order: Priority ASC, EffectiveFromUtc DESC NULLS LAST; take first. Fare formula uses BaseFare, PricePerKm, PricePerMinute, MinimumFare.

**Layer 6: Tenant Enforcement**  
RequireTenant on both controllers ensures ITenantContext.TenantId set. GetApplicableTariffAsync filters by tenantId; no cross-tenant data.

**Layer 7: Repository/DB**  
ITariffPlanRepository.GetApplicableTariffAsync (read-only query on tariff_plans). No writes.

**Layer 8: Outbox**  
Not used in QuoteFare.

**Layer 9: Audit**  
Not used in QuoteFare.

**Layer 10: Response mapping**  
200 with QuoteFareDto (FareAmount, Currency, TariffPlanId). 404 NO_ACTIVE_TARIFF or NO_APPLICABLE_TARIFF. 400 for other failures.
