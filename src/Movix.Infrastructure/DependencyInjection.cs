using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Movix.Application.Common.Interfaces;
using Movix.Application.Auth;
using Movix.Application.Trips;
using Movix.Application.Drivers;
using Movix.Application.Payments;
using Movix.Application.Admin;
using Movix.Infrastructure.Auth;
using Movix.Infrastructure.Persistence;
using Movix.Infrastructure.Persistence.Interceptors;
using Movix.Infrastructure.Persistence.Repositories;
using Movix.Infrastructure.Services;
using StackExchange.Redis;

namespace Movix.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AuditInterceptor>();
        var conn = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionString DefaultConnection not set");
        services.AddDbContext<MovixDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetRequiredService<AuditInterceptor>());
            options.UseNpgsql(conn, npgsql =>
            {
                npgsql.UseNetTopologySuite();
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public");
            });
        });

        var redisConn = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var config = ConfigurationOptions.Parse(redisConn);
            return ConnectionMultiplexer.Connect(config);
        });
        services.AddScoped<IIdempotencyService, RedisIdempotencyService>();

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddScoped<IAuthService, AuthService>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IDateTimeService, DateTimeService>();

        services.AddScoped<ITripRepository, TripRepository>();
        services.AddScoped<IDriverRepository, DriverRepository>();
        services.AddScoped<IDriverLocationRepository, DriverLocationRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IAdminTripRepository, AdminTripRepository>();

        return services;
    }
}
