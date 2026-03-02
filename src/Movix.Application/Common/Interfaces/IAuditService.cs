namespace Movix.Application.Common.Interfaces;

public interface IAuditService
{
    Task LogAsync(
        string action,
        string entityType,
        Guid? entityId,
        object? metadata,
        CancellationToken ct = default);
}
