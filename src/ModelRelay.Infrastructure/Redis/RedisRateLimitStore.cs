using ModelRelay.Core.Abstractions;
using StackExchange.Redis;

namespace ModelRelay.Infrastructure.Redis;

public sealed class RedisRateLimitStore : IRateLimitStore
{
    private readonly IDatabase _database;
    private readonly TimeProvider _timeProvider;

    public RedisRateLimitStore(IConnectionMultiplexer connection, TimeProvider? timeProvider = null)
    {
        _database = connection.GetDatabase();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<bool> TryConsumeAsync(Guid tenantId, int requestsPerMinute, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = _timeProvider.GetUtcNow();
        var minute = now.ToUnixTimeSeconds() / 60;
        var key = $"modelrelay:ratelimit:{tenantId:N}:{minute}";

        var count = await _database.StringIncrementAsync(key);
        if (count == 1)
        {
            await _database.KeyExpireAsync(key, TimeSpan.FromMinutes(2));
        }

        return count <= requestsPerMinute;
    }
}
