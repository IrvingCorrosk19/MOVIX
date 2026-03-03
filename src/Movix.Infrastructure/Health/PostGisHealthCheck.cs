using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Movix.Infrastructure.Persistence;

namespace Movix.Infrastructure.Health;

/// <summary>
/// Validates that PostGIS extension is installed and usable (e.g. SELECT PostGIS_Version()).
/// </summary>
public sealed class PostGisHealthCheck : IHealthCheck
{
    private readonly MovixDbContext _db;

    public PostGisHealthCheck(MovixDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT PostGIS_Version();";
            var version = (await cmd.ExecuteScalarAsync(cancellationToken))?.ToString();

            if (string.IsNullOrEmpty(version))
                return HealthCheckResult.Unhealthy("PostGIS_Version() returned empty.", data: new Dictionary<string, object> { ["postgis"] = "missing" });

            return HealthCheckResult.Healthy("PostGIS is available.", data: new Dictionary<string, object> { ["postgis_version"] = version });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostGIS check failed.", ex, new Dictionary<string, object> { ["error"] = ex.Message });
        }
    }
}
