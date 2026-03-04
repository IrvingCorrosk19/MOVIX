using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Movix.Api.Middleware;
using Movix.Domain.Enums;
using Xunit;

namespace Movix.Api.Tests.Middleware;

/// <summary>
/// BUG-005: When X-Tenant-Id header is present and does not match the JWT tenant_id claim (non-SuperAdmin),
/// the middleware must respond 403 with code TENANT_MISMATCH. No DB is hit (short-circuit before tenant validation).
/// </summary>
public class TenantMiddleware_TenantMismatch_Tests
{
    [Fact]
    public async Task When_header_tenant_differs_from_claim_returns_403_TENANT_MISMATCH()
    {
        var tenantFromClaim = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var tenantFromHeader = Guid.Parse("00000000-0000-0000-0000-000000000099");

        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new TenantMiddleware(next);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tenant_id", tenantFromClaim.ToString()),
            new Claim(ClaimTypes.Role, Role.Driver.ToString())
        }, "Test"));
        context.Request.Headers[TenantMiddleware.HeaderName] = tenantFromHeader.ToString();

        var services = new ServiceCollection();
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        await middleware.InvokeAsync(context, scopeFactory);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        Assert.Contains("TENANT_MISMATCH", body);
    }
}
