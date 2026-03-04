using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Movix.Domain.Enums;
using Movix.Infrastructure.Persistence;

namespace Movix.Api.Middleware;

public class TenantMiddleware
{
    public const string HeaderName = "X-Tenant-Id";
    public const string ItemKey = "TenantId";
    public const string IsSuperAdminKey = "IsSuperAdmin";
    public const string TenantInvalidKey = "TenantInvalid";

    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IServiceScopeFactory scopeFactory)
    {
        Guid? tenantId = null;
        var isSuperAdmin = false;

        // ── 1. Primary source: JWT claim tenant_id ─────────────────────────────
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var tenantClaim = context.User.FindFirst("tenant_id")?.Value;
            if (Guid.TryParse(tenantClaim, out var claimTenantId))
                tenantId = claimTenantId;

            var roleClaim = context.User.FindFirst(ClaimTypes.Role)?.Value;
            isSuperAdmin = string.Equals(roleClaim, Role.SuperAdmin.ToString(), StringComparison.Ordinal);
        }

        // ── 2. SuperAdmin override: header may replace the JWT tenant ──────────
        if (isSuperAdmin)
        {
            var headerValue = context.Request.Headers[HeaderName].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                if (Guid.TryParse(headerValue, out var headerTenantId))
                {
                    tenantId = headerTenantId;
                }
                else
                {
                    context.Items[TenantInvalidKey] = true;
                    await _next(context);
                    return;
                }
            }
        }
        else
        {
            // BUG-005: Non-SuperAdmin — if X-Tenant-Id is sent, it must match JWT tenant_id claim.
            var headerValue = context.Request.Headers[HeaderName].FirstOrDefault();
            if (tenantId.HasValue && !string.IsNullOrWhiteSpace(headerValue))
            {
                if (Guid.TryParse(headerValue, out var headerTenantId) && headerTenantId != tenantId.Value)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(
                        new { error = "X-Tenant-Id does not match the tenant in your token.", code = "TENANT_MISMATCH" },
                        context.RequestAborted);
                    return;
                }
            }
        }

        // ── 3. Validate tenant exists and is active ────────────────────────────
        if (tenantId.HasValue)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MovixDbContext>();

            var tenant = await db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantId.Value, context.RequestAborted);

            if (tenant == null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(
                    new { error = "Tenant not found", code = "TENANT_NOT_FOUND" },
                    context.RequestAborted);
                return;
            }

            if (!tenant.IsActive)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    new { error = "Tenant is inactive", code = "TENANT_INACTIVE" },
                    context.RequestAborted);
                return;
            }

            context.Items[ItemKey] = tenantId.Value;
            context.Items[IsSuperAdminKey] = isSuperAdmin;
        }

        await _next(context);
    }
}
