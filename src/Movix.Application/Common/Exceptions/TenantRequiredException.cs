namespace Movix.Application.Common.Exceptions;

public sealed class TenantRequiredException : Exception
{
    public const string Code = "TENANT_REQUIRED";

    public TenantRequiredException()
        : base("Tenant context is required.") { }
}
