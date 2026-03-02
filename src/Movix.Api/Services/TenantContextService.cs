using Movix.Application.Common.Interfaces;
using Movix.Api.Middleware;

namespace Movix.Api.Services;

public class TenantContextService : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantContextService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? TenantId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.Items[TenantMiddleware.ItemKey];
            return value is Guid g ? g : null;
        }
    }

    public bool IsSuperAdmin
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.Items[TenantMiddleware.IsSuperAdminKey];
            return value is true;
        }
    }
}
