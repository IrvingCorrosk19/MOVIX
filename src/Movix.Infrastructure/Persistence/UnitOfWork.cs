using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Movix.Application.Common.Exceptions;
using Movix.Application.Common.Interfaces;

namespace Movix.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly MovixDbContext _db;
    private readonly ILogger<UnitOfWork>? _logger;

    public UnitOfWork(MovixDbContext db, ILogger<UnitOfWork>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            foreach (var entry in ex.Entries)
            {
                var entityName = entry.Metadata.Name;
                var pk = GetPrimaryKeyValue(entry);
                var tokenInfo = GetConcurrencyTokenValues(entry);
                _logger?.LogWarning(
                    "Concurrency conflict. Entity={EntityName} PrimaryKey={PrimaryKey} State={State} ConcurrencyToken={TokenInfo}",
                    entityName, pk, entry.State, tokenInfo);
            }
            throw new ConcurrencyException("Concurrency conflict.", ex);
        }
    }

    /// <summary>Returns the primary key value(s) of the entity entry for forensic logging.</summary>
    private static string GetPrimaryKeyValue(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key == null) return "(no key)";
        var parts = new List<string>();
        foreach (var prop in key.Properties)
        {
            var val = entry.Property(prop.Name).CurrentValue;
            parts.Add($"{prop.Name}={FormatValue(val)}");
        }
        return string.Join(", ", parts);
    }

    /// <summary>Returns original and current concurrency token values for forensic logging (byte[] as HEX, uint as value).</summary>
    private static string GetConcurrencyTokenValues(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        var tokens = entry.Metadata.GetProperties().Where(p => p.IsConcurrencyToken).ToList();
        if (tokens.Count == 0) return "(no token)";
        var parts = new List<string>();
        foreach (var prop in tokens)
        {
            var orig = entry.Property(prop.Name).OriginalValue;
            var curr = entry.Property(prop.Name).CurrentValue;
            parts.Add($"{prop.Name}: Original={FormatValue(orig)} Current={FormatValue(curr)}");
        }
        return string.Join("; ", parts);
    }

    private static string FormatValue(object? value)
    {
        if (value == null) return "null";
        if (value is byte[] bytes) return "0x" + Convert.ToHexString(bytes);
        if (value is uint u) return u.ToString();
        return value.ToString() ?? "?";
    }
}
