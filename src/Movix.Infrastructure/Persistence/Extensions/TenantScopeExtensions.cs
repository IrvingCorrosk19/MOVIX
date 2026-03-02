using Movix.Application.Common.Exceptions;
using Movix.Application.Common.Interfaces;
using Movix.Domain.Entities;

namespace Movix.Infrastructure.Persistence.Extensions;

/// <summary>
/// Extension methods that apply tenant-scoped WHERE clauses to EF Core queries.
/// Behaviour:
///   - IsSuperAdmin  → no filter (bypass, for cross-tenant operations)
///   - TenantId null → throws TenantRequiredException (TENANT_REQUIRED)
///   - TenantId set  → WHERE entity.TenantId = ctx.TenantId
/// </summary>
public static class TenantScopeExtensions
{
    /// <summary>Scope trip queries to the caller's tenant.</summary>
    public static IQueryable<Trip> ApplyTenantScope(this IQueryable<Trip> query, ITenantContext ctx)
    {
        if (ctx.IsSuperAdmin) return query;
        if (!ctx.TenantId.HasValue) throw new TenantRequiredException();
        return query.Where(t => t.TenantId == ctx.TenantId.Value);
    }

    /// <summary>Scope driver queries to the caller's tenant.</summary>
    public static IQueryable<Driver> ApplyTenantScope(this IQueryable<Driver> query, ITenantContext ctx)
    {
        if (ctx.IsSuperAdmin) return query;
        if (!ctx.TenantId.HasValue) throw new TenantRequiredException();
        return query.Where(d => d.TenantId == ctx.TenantId.Value);
    }
}
