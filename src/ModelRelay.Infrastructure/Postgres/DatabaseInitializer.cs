using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace ModelRelay.Infrastructure.Postgres;

public sealed class DatabaseInitializer
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _demoApiKey;

    public DatabaseInitializer(NpgsqlDataSource dataSource, string demoApiKey)
    {
        _dataSource = dataSource;
        _demoApiKey = demoApiKey;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var assembly = typeof(DatabaseInitializer).Assembly;
        var migrationNames = assembly.GetManifestResourceNames()
            .Where(x => x.Contains(".Migrations.", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        foreach (var resourceName in migrationNames)
        {
            await using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded migration '{resourceName}' was not found.");
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(cancellationToken);
            await using var command = _dataSource.CreateCommand(sql);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_demoApiKey))).ToLowerInvariant();

        await using var seed = _dataSource.CreateCommand("""
            INSERT INTO tenants (id, name, api_key_hash, monthly_budget_microusd, requests_per_minute)
            VALUES ('11111111-1111-1111-1111-111111111111', 'Local Demo', $1, 250000, 60)
            ON CONFLICT (id) DO UPDATE
            SET api_key_hash = EXCLUDED.api_key_hash;
            """);
        seed.Parameters.AddWithValue(hash);
        await seed.ExecuteNonQueryAsync(cancellationToken);
    }
}
