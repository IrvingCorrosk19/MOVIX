using Microsoft.EntityFrameworkCore;
using Movix.Domain.Entities;

namespace Movix.Infrastructure.Persistence;

public class MovixDbContext : DbContext
{
    public MovixDbContext(DbContextOptions<MovixDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<TripStatusHistory> TripStatusHistories => Set<TripStatusHistory>();
    public DbSet<DriverLocationLive> DriverLocationLives => Set<DriverLocationLive>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof(MovixDbContext).Assembly);
    }
}
