# Flow: QuoteFare

1. **HTTP**  
   - TariffsController: GET `/api/v1/admin/tariffs/fare/quote?distanceKm=&durationMin=` (Admin/Support, RequireTenant).  
   - FareController: GET `/api/v1/fare/quote?distanceKm=&durationMin=` (Authorize, RequireTenant).  
   Both use same handler and query.

2. **Controller**  
   - TenantId from `ITenantContext.TenantId` (required by RequireTenant).  
   - Sends `QuoteFareQuery(tenantId, distanceKm, durationMin)`.  
   - On failure: NO_ACTIVE_TARIFF or NO_APPLICABLE_TARIFF -> 404; else 400.

3. **Handler (QuoteFareQueryHandler)**  
   - **Time:** `IDateTimeService.UtcNow` -> atUtc.  
   - **Tariff:** `ITariffPlanRepository.GetApplicableTariffAsync(tenantId, atUtc, ct)`.  
     - Filter: TenantId, IsActive, (EffectiveFromUtc null or <= atUtc), (EffectiveUntilUtc null or > atUtc).  
     - Order: Priority ASC, EffectiveFromUtc DESC NULLS LAST. Take first.  
   - If null, return NO_APPLICABLE_TARIFF.  
   - **Fare:** `IFareCalculator.Calculate(plan.BaseFare, plan.PricePerKm, plan.PricePerMinute, plan.MinimumFare, distanceKm, durationMinutes)`.  
   - Return `Result.Success(new QuoteFareDto(fare, plan.Currency, plan.Id))`.

4. **DB impact**  
   - Read-only: single query to tariff_plans (or equivalent via GetApplicableTariffAsync).

5. **Response**  
   - 200: QuoteFareDto (FareAmount, Currency, TariffPlanId).  
   - 404: NO_APPLICABLE_TARIFF / NO_ACTIVE_TARIFF.  
   - 400: other.
