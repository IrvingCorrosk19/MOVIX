using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Movix.Domain.Entities;
using Movix.Domain.Enums;

namespace Movix.Infrastructure.Persistence;

public class DataSeeder
{
    private const string AdminEmailKey = "ADMIN_EMAIL";
    private const string AdminPasswordKey = "ADMIN_PASSWORD";
    private const string DriverEmailKey = "DRIVER_EMAIL";
    private const string DriverPasswordKey = "DRIVER_PASSWORD";

    private readonly IConfiguration _configuration;

    public DataSeeder(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SeedAsync(MovixDbContext db, string environmentName, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
            return;

        var adminEmail = _configuration[AdminEmailKey];
        var adminPassword = _configuration[AdminPasswordKey];
        if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
            await EnsureAdminUserAsync(db, adminEmail, adminPassword, cancellationToken);

        var driverEmail = _configuration[DriverEmailKey];
        var driverPassword = _configuration[DriverPasswordKey];
        if (!string.IsNullOrWhiteSpace(driverEmail) && !string.IsNullOrWhiteSpace(driverPassword))
            await EnsureDriverWithVehicleAsync(db, driverEmail, driverPassword, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureAdminUserAsync(MovixDbContext db, string email, string password, CancellationToken ct)
    {
        var exists = await db.Users.AnyAsync(u => u.Email == email, ct);
        if (exists) return;

        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = Role.Admin,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = "Seed",
            UpdatedBy = "Seed",
            RowVersion = new byte[] { 1 }
        };
        db.Users.Add(user);
    }

    private static async Task EnsureDriverWithVehicleAsync(MovixDbContext db, string email, string password, CancellationToken ct)
    {
        var existingUser = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (existingUser != null)
        {
            if (existingUser.Role != Role.Driver)
            {
                existingUser.Role = Role.Driver;
                existingUser.UpdatedAtUtc = DateTime.UtcNow;
                existingUser.UpdatedBy = "Seed";
            }
            var hasDriver = await db.Drivers.AnyAsync(d => d.UserId == existingUser.Id, ct);
            if (hasDriver) return;

            var now = DateTime.UtcNow;
            var driver = new Driver
            {
                Id = Guid.NewGuid(),
                UserId = existingUser.Id,
                Status = DriverStatus.Offline,
                IsVerified = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CreatedBy = "Seed",
                UpdatedBy = "Seed",
                RowVersion = new byte[] { 1 }
            };
            driver.Vehicles.Add(new Vehicle
            {
                Id = Guid.NewGuid(),
                DriverId = driver.Id,
                Plate = "SEED-001",
                Model = "Example",
                Color = "White",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CreatedBy = "Seed",
                UpdatedBy = "Seed",
                RowVersion = new byte[] { 1 }
            });
            db.Drivers.Add(driver);
            return;
        }

        var now2 = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = Role.Driver,
            IsActive = true,
            CreatedAtUtc = now2,
            UpdatedAtUtc = now2,
            CreatedBy = "Seed",
            UpdatedBy = "Seed",
            RowVersion = new byte[] { 1 }
        };
        db.Users.Add(user);

        var driverEntity = new Driver
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Status = DriverStatus.Offline,
            IsVerified = true,
            CreatedAtUtc = now2,
            UpdatedAtUtc = now2,
            CreatedBy = "Seed",
            UpdatedBy = "Seed",
            RowVersion = new byte[] { 1 }
        };
        driverEntity.Vehicles.Add(new Vehicle
        {
            Id = Guid.NewGuid(),
            DriverId = driverEntity.Id,
            Plate = "SEED-001",
            Model = "Example",
            Color = "White",
            IsActive = true,
            CreatedAtUtc = now2,
            UpdatedAtUtc = now2,
            CreatedBy = "Seed",
            UpdatedBy = "Seed",
            RowVersion = new byte[] { 1 }
        });
        db.Drivers.Add(driverEntity);
    }
}
