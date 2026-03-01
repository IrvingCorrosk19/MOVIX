using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Movix.Api.Tests.Startup;

public class JwtSecretKeyValidationTests
{
    private static void AddMinimalConfig(IConfigurationBuilder config, string jwtSecretKeyValue)
    {
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=movix;Username=postgres;Password=test;",
            ["ConnectionStrings:Redis"] = "localhost:6379",
            ["Jwt:SecretKey"] = jwtSecretKeyValue
        });
    }

    [Fact]
    public void Startup_WithMissingJwtSecretKey_ThrowsInvalidOperationException()
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, config) => AddMinimalConfig(config, ""));
            });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.Server);
        Assert.Contains("JWT SecretKey must be provided via environment variable and be at least 32 characters.", ex.Message);
    }

    [Fact]
    public void Startup_WithShortJwtSecretKey_ThrowsInvalidOperationException()
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, config) => AddMinimalConfig(config, "short"));
            });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.Server);
        Assert.Contains("JWT SecretKey must be provided via environment variable and be at least 32 characters.", ex.Message);
    }

    [Fact]
    public void Startup_WithChangeMeInJwtSecretKey_ThrowsInvalidOperationException()
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, config) => AddMinimalConfig(config, "MOVIX_CHANGE_ME_MIN_32_CHARS_FOR_HS256"));
            });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.Server);
        Assert.Contains("JWT SecretKey must be provided via environment variable and be at least 32 characters.", ex.Message);
    }
}
