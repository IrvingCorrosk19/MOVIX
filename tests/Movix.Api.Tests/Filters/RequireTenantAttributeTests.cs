using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Movix.Api.Filters;
using Movix.Api.Middleware;
using Xunit;

namespace Movix.Api.Tests.Filters;

public class RequireTenantAttributeTests
{
    [Fact]
    public async Task OnActionExecutionAsync_WhenTenantIdMissing_Returns400WithTENANT_REQUIRED()
    {
        var filter = new RequireTenantAttribute();
        var httpContext = new DefaultHttpContext();
        httpContext.Items.Remove(TenantMiddleware.ItemKey);
        var actionContext = new ActionExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            null!);
        ActionExecutionDelegate next = () => throw new InvalidOperationException("Should not be called");

        await filter.OnActionExecutionAsync(actionContext, next);

        var result = actionContext.Result as BadRequestObjectResult;
        Assert.NotNull(result);
        var value = result!.Value;
        var code = value?.GetType().GetProperty("code")?.GetValue(value) as string;
        Assert.Equal("TENANT_REQUIRED", code);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenTenantIdInvalid_Returns400WithTENANT_INVALID()
    {
        var filter = new RequireTenantAttribute();
        var httpContext = new DefaultHttpContext();
        httpContext.Items[TenantMiddleware.TenantInvalidKey] = true;
        httpContext.Items.Remove(TenantMiddleware.ItemKey);
        var actionContext = new ActionExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            null!);
        ActionExecutionDelegate next = () => throw new InvalidOperationException("Should not be called");

        await filter.OnActionExecutionAsync(actionContext, next);

        var result = actionContext.Result as BadRequestObjectResult;
        Assert.NotNull(result);
        var value = result!.Value;
        var code = value?.GetType().GetProperty("code")?.GetValue(value) as string;
        Assert.Equal("TENANT_INVALID", code);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenTenantIdPresent_InvokesNext()
    {
        var filter = new RequireTenantAttribute();
        var httpContext = new DefaultHttpContext();
        httpContext.Items[TenantMiddleware.ItemKey] = Guid.NewGuid();
        var actionContext = new ActionExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            null!);
        var nextCalled = false;
        ActionExecutionDelegate next = () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), null!) { Result = new OkResult() });
        };

        await filter.OnActionExecutionAsync(actionContext, next);

        Assert.True(nextCalled);
    }
}
