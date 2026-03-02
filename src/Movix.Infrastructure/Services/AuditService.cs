using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Movix.Application.Common.Interfaces;
using Movix.Domain.Entities;
using Movix.Infrastructure.Persistence;

namespace Movix.Infrastructure.Services;

public class AuditService : IAuditService
{
    private readonly MovixDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeService _dateTime;

    public AuditService(
        MovixDbContext db,
        ITenantContext tenant,
        ICurrentUserService currentUser,
        IDateTimeService dateTime)
    {
        _db = db;
        _tenant = tenant;
        _currentUser = currentUser;
        _dateTime = dateTime;
    }

    public async Task LogAsync(
        string action,
        string entityType,
        Guid? entityId,
        object? metadata,
        CancellationToken ct = default)
    {
        var metadataJson = metadata != null ? JsonSerializer.Serialize(metadata) : null;
        var log = new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId ?? Guid.Empty,
            UserId = _currentUser.UserId,
            Role = _currentUser.Role?.ToString() ?? "",
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Metadata = metadataJson,
            CreatedAtUtc = _dateTime.UtcNow
        };
        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync(ct);
    }
}
