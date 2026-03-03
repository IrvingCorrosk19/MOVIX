using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Movix.Infrastructure.Persistence;

public class MovixDbContextFactory : IDesignTimeDbContextFactory<MovixDbContext>
{
    public MovixDbContext CreateDbContext(string[] args)
    {
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "Movix.Api");
        var config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddJsonFile("appsettings.Development.local.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<MovixDbContext>();
        var conn = config.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=movix_core;Username=movix;Password=movix_secret";
        optionsBuilder.UseNpgsql(conn, npgsql =>
        {
            npgsql.UseNetTopologySuite();
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public");
        });

        return new MovixDbContext(optionsBuilder.Options);
    }
}
