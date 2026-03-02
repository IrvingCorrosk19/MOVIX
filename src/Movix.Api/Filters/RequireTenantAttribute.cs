using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Movix.Api.Middleware;

namespace Movix.Api.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireTenantAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.HttpContext.Items[TenantMiddleware.TenantInvalidKey] is true)
        {
            context.Result = new BadRequestObjectResult(new { error = "X-Tenant-Id must be a valid Guid.", code = "TENANT_INVALID" });
            return;
        }
        var tenantId = context.HttpContext.Items[TenantMiddleware.ItemKey];
        if (tenantId == null)
        {
            context.Result = new BadRequestObjectResult(new { error = "X-Tenant-Id header is required and must be a valid Guid.", code = "TENANT_REQUIRED" });
            return;
        }
        await next();
    }
}
