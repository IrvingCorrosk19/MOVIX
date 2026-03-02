# Flow: ActivateTariffPlan

1. **HTTP**  
   POST `/api/v1/admin/tariffs/{id}/activate`. No body. Tenant from context (RequireTenant on controller).

2. **Controller**  
   - `[Authorize(Roles = "Admin,Support")]`, `[RequireTenant]`.  
   - tenantId = `ITenantContext.TenantId!.Value`.  
   - Sends `ActivateTariffPlanCommand(tenantId, id)`.  
   - TARIFF_NOT_FOUND -> 404, TARIFF_CROSS_TENANT -> 403, TARIFF_PRIORITY_CONFLICT -> 400, else 400.

3. **Handler (ActivateTariffPlanCommandHandler)**  
   - **Load plan:** `ITariffPlanRepository.GetByIdAsync(request.TariffPlanId)`. Null -> TARIFF_NOT_FOUND.  
   - **Tenant:** plan.TenantId != request.TenantId -> TARIFF_CROSS_TENANT.  
   - **Conflict:** `HasOverlappingSamePriorityAsync(tenantId, plan.Id, plan.Priority, plan.EffectiveFromUtc, plan.EffectiveUntilUtc)`. True -> TARIFF_PRIORITY_CONFLICT (another active plan with same priority and overlapping effective window).  
   - **Update:** plan.IsActive = true. No deactivation of other plans.  
   - **Persistence:** `IUnitOfWork.SaveChangesAsync()`.  
   - **Audit:** `IAuditService.LogAsync("ActivateTariffPlan", "TariffPlan", plan.Id, new { plan.Priority })`.  
   - Return `Result.Success(Map(plan))` (TariffPlanDto).

4. **DB impact**  
   - `tariff_plans`: IsActive = true for the specified plan only.  
   - `audit_logs`: one new row.

5. **Response**  
   - 200: TariffPlanDto.  
   - 404 TARIFF_NOT_FOUND, 403 TARIFF_CROSS_TENANT, 400 TARIFF_PRIORITY_CONFLICT or other.
