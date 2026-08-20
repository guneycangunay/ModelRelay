using System.Data;
using ModelRelay.Core.Abstractions;
using ModelRelay.Core.Billing;
using ModelRelay.Core.Models;
using Npgsql;

namespace ModelRelay.Infrastructure.Postgres;

public sealed class PostgresStore : ITenantStore, IUsageStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresStore(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    public async Task<Tenant?> FindByApiKeyHashAsync(string apiKeyHash, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand("""
            SELECT id, name, monthly_budget_microusd, requests_per_minute
            FROM tenants
            WHERE api_key_hash = $1
            LIMIT 1;
            """);
        command.Parameters.AddWithValue(apiKeyHash);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new Tenant(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetInt32(3));
    }

    public async Task<BudgetReservation?> TryReserveBudgetAsync(
        Tenant tenant,
        string model,
        int estimatedPromptTokens,
        int maxCompletionTokens,
        CancellationToken cancellationToken)
    {
        var amount = ModelPricing.EstimateMaximumCost(model, estimatedPromptTokens, maxCompletionTokens);
        var now = DateTimeOffset.UtcNow;
        var monthKey = now.ToString("yyyy-MM");
        var reservationId = Guid.NewGuid();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        await using (var ensureMonth = new NpgsqlCommand("""
            INSERT INTO tenant_budget_month (tenant_id, month_key, spent_microusd)
            VALUES ($1, $2, 0)
            ON CONFLICT (tenant_id, month_key) DO NOTHING;
            """, connection, transaction))
        {
            ensureMonth.Parameters.AddWithValue(tenant.Id);
            ensureMonth.Parameters.AddWithValue(monthKey);
            await ensureMonth.ExecuteNonQueryAsync(cancellationToken);
        }

        long spent;
        await using (var lockMonth = new NpgsqlCommand("""
            SELECT spent_microusd
            FROM tenant_budget_month
            WHERE tenant_id = $1 AND month_key = $2
            FOR UPDATE;
            """, connection, transaction))
        {
            lockMonth.Parameters.AddWithValue(tenant.Id);
            lockMonth.Parameters.AddWithValue(monthKey);
            spent = (long)(await lockMonth.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }

        await using (var cleanup = new NpgsqlCommand("""
            DELETE FROM budget_reservations
            WHERE tenant_id = $1
              AND month_key = $2
              AND created_at < now() - interval '15 minutes';
            """, connection, transaction))
        {
            cleanup.Parameters.AddWithValue(tenant.Id);
            cleanup.Parameters.AddWithValue(monthKey);
            await cleanup.ExecuteNonQueryAsync(cancellationToken);
        }

        long activeReserved;
        await using (var reserved = new NpgsqlCommand("""
            SELECT COALESCE(SUM(amount_microusd), 0)
            FROM budget_reservations
            WHERE tenant_id = $1
              AND month_key = $2
              AND created_at >= now() - interval '15 minutes';
            """, connection, transaction))
        {
            reserved.Parameters.AddWithValue(tenant.Id);
            reserved.Parameters.AddWithValue(monthKey);
            activeReserved = (long)(await reserved.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }

        if (spent + activeReserved + amount > tenant.MonthlyBudgetMicroUsd)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await using (var insert = new NpgsqlCommand("""
            INSERT INTO budget_reservations (id, tenant_id, month_key, amount_microusd, created_at)
            VALUES ($1, $2, $3, $4, now());
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue(reservationId);
            insert.Parameters.AddWithValue(tenant.Id);
            insert.Parameters.AddWithValue(monthKey);
            insert.Parameters.AddWithValue(amount);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new BudgetReservation(reservationId, tenant.Id, monthKey, amount);
    }

    public async Task FinalizeUsageAsync(
        BudgetReservation reservation,
        UsageRecord usage,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var insertUsage = new NpgsqlCommand("""
            INSERT INTO usage_requests (
                request_id, tenant_id, model, provider, prompt_tokens, completion_tokens,
                cost_microusd, latency_ms, fallback_count, redaction_count, status, created_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,now());
            """, connection, transaction))
        {
            insertUsage.Parameters.AddWithValue(usage.RequestId);
            insertUsage.Parameters.AddWithValue(usage.TenantId);
            insertUsage.Parameters.AddWithValue(usage.Model);
            insertUsage.Parameters.AddWithValue(usage.Provider);
            insertUsage.Parameters.AddWithValue(usage.PromptTokens);
            insertUsage.Parameters.AddWithValue(usage.CompletionTokens);
            insertUsage.Parameters.AddWithValue(usage.CostMicroUsd);
            insertUsage.Parameters.AddWithValue(usage.LatencyMs);
            insertUsage.Parameters.AddWithValue(usage.FallbackCount);
            insertUsage.Parameters.AddWithValue(usage.RedactionCount);
            insertUsage.Parameters.AddWithValue(usage.Status);
            await insertUsage.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var spend = new NpgsqlCommand("""
            UPDATE tenant_budget_month
            SET spent_microusd = spent_microusd + $3
            WHERE tenant_id = $1 AND month_key = $2;
            """, connection, transaction))
        {
            spend.Parameters.AddWithValue(reservation.TenantId);
            spend.Parameters.AddWithValue(reservation.MonthKey);
            spend.Parameters.AddWithValue(usage.CostMicroUsd);
            await spend.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteReservation = new NpgsqlCommand(
            "DELETE FROM budget_reservations WHERE id = $1;",
            connection,
            transaction))
        {
            deleteReservation.Parameters.AddWithValue(reservation.Id);
            await deleteReservation.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReleaseReservationAsync(BudgetReservation reservation, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand("DELETE FROM budget_reservations WHERE id = $1;");
        command.Parameters.AddWithValue(reservation.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DashboardSummary> GetDashboardSummaryAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        long requests24h;
        long tokens24h;
        long fallbacks24h;
        double p95;
        await using (var summary = new NpgsqlCommand("""
            SELECT
              COUNT(*) FILTER (WHERE created_at >= now() - interval '24 hours'),
              COALESCE(SUM(prompt_tokens + completion_tokens) FILTER (WHERE created_at >= now() - interval '24 hours'), 0),
              COALESCE(SUM(fallback_count) FILTER (WHERE created_at >= now() - interval '24 hours'), 0),
              COALESCE(percentile_cont(0.95) WITHIN GROUP (ORDER BY latency_ms)
                FILTER (WHERE created_at >= now() - interval '24 hours'), 0)
            FROM usage_requests
            WHERE tenant_id = $1;
            """, connection))
        {
            summary.Parameters.AddWithValue(tenantId);
            await using var reader = await summary.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            requests24h = reader.GetInt64(0);
            tokens24h = reader.GetInt64(1);
            fallbacks24h = reader.GetInt64(2);
            p95 = Convert.ToDouble(reader.GetValue(3), System.Globalization.CultureInfo.InvariantCulture);
        }

        long spent;
        long budget;
        await using (var month = new NpgsqlCommand("""
            SELECT
              COALESCE(b.spent_microusd, 0),
              t.monthly_budget_microusd
            FROM tenants t
            LEFT JOIN tenant_budget_month b
              ON b.tenant_id = t.id AND b.month_key = to_char(now(), 'YYYY-MM')
            WHERE t.id = $1;
            """, connection))
        {
            month.Parameters.AddWithValue(tenantId);
            await using var reader = await month.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            spent = reader.GetInt64(0);
            budget = reader.GetInt64(1);
        }

        var providers = new List<ProviderSummary>();
        await using (var providerQuery = new NpgsqlCommand("""
            SELECT provider,
                   COUNT(*),
                   COALESCE(AVG(latency_ms), 0)
            FROM usage_requests
            WHERE tenant_id = $1 AND created_at >= now() - interval '24 hours'
            GROUP BY provider
            ORDER BY COUNT(*) DESC;
            """, connection))
        {
            providerQuery.Parameters.AddWithValue(tenantId);
            await using var reader = await providerQuery.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                providers.Add(new ProviderSummary(
                    reader.GetString(0),
                    reader.GetInt64(1),
                    Convert.ToDouble(reader.GetValue(2), System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        return new DashboardSummary(requests24h, tokens24h, spent, budget, p95, fallbacks24h, providers);
    }
}
