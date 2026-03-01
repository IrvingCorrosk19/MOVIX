using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Movix.Application.Common.Interfaces;

namespace Movix.Infrastructure.Persistence.Interceptors;

public class AuditInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService? _currentUser;

    public AuditInterceptor(ICurrentUserService? currentUser)
    {
        _currentUser = currentUser;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        SetAuditFields(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        SetAuditFields(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void SetAuditFields(DbContext? context)
    {
        if (context == null) return;
        var now = DateTime.UtcNow;
        var userId = _currentUser?.UserId.ToString();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Metadata.FindProperty("CreatedAtUtc") != null)
                    entry.Property("CreatedAtUtc").CurrentValue = now;
                if (entry.Metadata.FindProperty("UpdatedAtUtc") != null)
                    entry.Property("UpdatedAtUtc").CurrentValue = now;
                if (entry.Metadata.FindProperty("CreatedBy") != null && userId != null)
                    entry.Property("CreatedBy").CurrentValue = userId;
                if (entry.Metadata.FindProperty("UpdatedBy") != null && userId != null)
                    entry.Property("UpdatedBy").CurrentValue = userId;
            }
            else if (entry.State == EntityState.Modified)
            {
                if (entry.Metadata.FindProperty("UpdatedAtUtc") != null)
                    entry.Property("UpdatedAtUtc").CurrentValue = now;
                if (entry.Metadata.FindProperty("UpdatedBy") != null && userId != null)
                    entry.Property("UpdatedBy").CurrentValue = userId;
            }
        }
    }
}
