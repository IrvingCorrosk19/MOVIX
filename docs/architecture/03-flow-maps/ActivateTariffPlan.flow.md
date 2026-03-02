# Flow: ActivateTariffPlan

**Layer 1: HTTP Layer**  
POST `/api/v1/admin/tariffs/{id}/activate`. Route id = TariffPlanId. No body. Tenant from context (RequireTenant on controller).

**Layer 2: Controller**  
TariffsController.Activate. [Authorize(Roles = "Admin,Support")], [RequireTenant]. tenantId = ITenantContext.TenantId!.Value. Sends ActivateTariffPlanCommand(tenantId, id). Maps: TARIFF_NOT_FOUND -> 404, TARIFF_CROSS_TENANT -> 403, TARIFF_PRIORITY_CONFLICT -> 400; else 400. Success -> Ok(result.Data).

**Layer 3: Command/Query**  
ActivateTariffPlanCommand(TenantId, TariffPlanId). IRequest<Result<TariffPlanDto>>.

**Layer 4: Handler**  
ActivateTariffPlanCommandHandler. Load plan by TariffPlanId; null -> TARIFF_NOT_FOUND. plan.TenantId != request.TenantId -> TARIFF_CROSS_TENANT. HasOverlappingSamePriorityAsync(tenantId, plan.Id, plan.Priority, plan.EffectiveFromUtc, plan.EffectiveUntilUtc) -> true then TARIFF_PRIORITY_CONFLICT. Set plan.IsActive = true (no deactivation of other plans). SaveChangesAsync. IAuditService.LogAsync("ActivateTariffPlan", "TariffPlan", plan.Id, new { plan.Priority }). Return Map(plan) TariffPlanDto.

**Layer 5: Domain Rules**  
Only one plan is toggled active. Overlap check: no other active plan with same Priority and overlapping effective window (EffectiveFromUtc/EffectiveUntilUtc). Multiple active plans per tenant allowed if priorities or windows differ.

**Layer 6: Tenant Enforcement**  
RequireTenant ensures TenantId in context. Handler validates plan.TenantId == request.TenantId (TARIFF_CROSS_TENANT otherwise). No cross-tenant activation.

**Layer 7: Repository/DB**  
ITariffPlanRepository.GetByIdAsync. ITariffPlanRepository.HasOverlappingSamePriorityAsync. IUnitOfWork.SaveChangesAsync. Writes: tariff_plans (IsActive = true for one row).

**Layer 8: Outbox**  
Not used in ActivateTariffPlan.

**Layer 9: Audit**  
IAuditService.LogAsync("ActivateTariffPlan", "TariffPlan", plan.Id, new { plan.Priority }). After SaveChangesAsync.

**Layer 10: Response mapping**  
200 with TariffPlanDto. 404 TARIFF_NOT_FOUND, 403 TARIFF_CROSS_TENANT, 400 TARIFF_PRIORITY_CONFLICT or other.
