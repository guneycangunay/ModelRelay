using ModelRelay.Core.Models;

namespace ModelRelay.Core.Abstractions;

public interface ILlmProvider
{
    string Name { get; }
    Task<ProviderResult> CompleteAsync(ProviderRequest request, CancellationToken cancellationToken);
}

public interface ITenantStore
{
    Task<Tenant?> FindByApiKeyHashAsync(string apiKeyHash, CancellationToken cancellationToken);
}

public interface IRateLimitStore
{
    Task<bool> TryConsumeAsync(Guid tenantId, int requestsPerMinute, CancellationToken cancellationToken);
}

public interface IUsageStore
{
    Task<BudgetReservation?> TryReserveBudgetAsync(
        Tenant tenant,
        string model,
        int estimatedPromptTokens,
        int maxCompletionTokens,
        CancellationToken cancellationToken);

    Task FinalizeUsageAsync(
        BudgetReservation reservation,
        UsageRecord usage,
        CancellationToken cancellationToken);

    Task ReleaseReservationAsync(BudgetReservation reservation, CancellationToken cancellationToken);

    Task<DashboardSummary> GetDashboardSummaryAsync(Guid tenantId, CancellationToken cancellationToken);
}
