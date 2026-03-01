using Movix.Application.Common.Interfaces;
using StackExchange.Redis;

namespace Movix.Infrastructure.Services;

public class RedisIdempotencyService : IIdempotencyService
{
    private const string KeyPrefix = "movix:idempotency:";
    private const int TtlHours = 24;
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;

    public RedisIdempotencyService(IConnectionMultiplexer redis)
    {
        _redis = redis;
        _db = redis.GetDatabase();
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = KeyPrefix + key;
        return await _db.KeyExistsAsync(fullKey);
    }

    public async Task StoreAsync(string key, string responsePayload, CancellationToken cancellationToken = default)
    {
        var fullKey = KeyPrefix + key;
        await _db.StringSetAsync(fullKey, responsePayload, TimeSpan.FromHours(TtlHours));
    }

    public async Task<string?> GetResponseAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = KeyPrefix + key;
        var value = await _db.StringGetAsync(fullKey);
        return value.HasValue ? value.ToString() : null;
    }
}
