using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Movix.Api.Extensions;
using Movix.Api.Middleware;
using Movix.Api.Services;
using Movix.Application;
using Movix.Application.Common.Interfaces;
using Movix.Infrastructure;
using Movix.Infrastructure.Auth;
using Movix.Infrastructure.Health;
using Movix.Infrastructure.Messaging;
using Movix.Infrastructure.Persistence;
using Movix.Infrastructure.Payments;
using Serilog;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Development.local.json", optional: true, reloadOnChange: true);

builder.Host.UseSerilog((ctx, lc) =>
{
    lc.ReadFrom.Configuration(ctx.Configuration)
      .Enrich.FromLogContext()
      .Enrich.WithProperty("Application", "movix-api")
      .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [movix] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}");
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IEventPublisher, LoggingEventPublisher>();
builder.Services.AddHostedService<OutboxHostedService>();

builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<ITenantContext, TenantContextService>();

var jwtSection = builder.Configuration.GetSection(JwtSettings.SectionName);
var secret = builder.Configuration["Jwt:SecretKey"];
if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32 || secret.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("JWT SecretKey must be provided via environment variable and be at least 32 characters.");
var paymentsMode = builder.Configuration["Payments:Mode"] ?? "Stripe";
if (!string.Equals(paymentsMode, "Simulation", StringComparison.OrdinalIgnoreCase))
{
    var stripeSecret = builder.Configuration["Stripe:SecretKey"];
    if (string.IsNullOrWhiteSpace(stripeSecret))
        throw new InvalidOperationException("Stripe SecretKey must be provided via environment variable (Stripe__SecretKey).");
    var stripeWebhookSecret = builder.Configuration["Stripe:WebhookSecret"];
    if (string.IsNullOrWhiteSpace(stripeWebhookSecret))
        throw new InvalidOperationException("Stripe WebhookSecret must be provided via environment variable (Stripe__WebhookSecret).");
}
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"] ?? "movix",
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"] ?? "movix",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.PermitLimit = 10;
    });
    options.AddFixedWindowLimiter("trips", limiter =>
    {
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.PermitLimit = 30;
    });
    options.AddFixedWindowLimiter("payments", limiter =>
    {
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.PermitLimit = 20;
    });
});

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection")!, name: "postgres", tags: new[] { "db", "ready" })
    .AddRedis(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379", name: "redis", tags: new[] { "redis", "ready" })
    .AddCheck<OutboxHealthCheck>("outbox");

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("movix-api"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation());

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "MOVIX API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "JWT Bearer",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
    });
});

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseSecurityHeaders();

app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "MOVIX API v1"));

app.UseRateLimiter();

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = (context, report) => context.Response.WriteAsJsonAsync(report)
});
app.MapHealthChecks("/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = (context, report) => context.Response.WriteAsJsonAsync(report)
});

// Authentication must run before TenantMiddleware so that context.User is populated
// and the tenant_id JWT claim can be read.
app.UseAuthentication();
app.UseMiddleware<TenantMiddleware>();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MovixDbContext>();
    await db.Database.MigrateAsync();
    var seeder = scope.ServiceProvider.GetRequiredService<Movix.Infrastructure.Persistence.DataSeeder>();
    await seeder.SeedAsync(db, app.Environment.EnvironmentName);
}

app.Run();

public partial class Program { }
