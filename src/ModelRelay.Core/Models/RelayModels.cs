namespace ModelRelay.Core.Models;

public sealed record Tenant(
    Guid Id,
    string Name,
    long MonthlyBudgetMicroUsd,
    int RequestsPerMinute);

public sealed record ProviderRequest(
    string RequestId,
    string Model,
    IReadOnlyList<(string Role, string Content)> Messages,
    int MaxTokens,
    double Temperature);

public sealed record ProviderResult(
    string Provider,
    string Content,
    int PromptTokens,
    int CompletionTokens,
    long LatencyMs);

public sealed record RelayResult(
    ProviderResult ProviderResult,
    int FallbackCount,
    int RedactionCount);

public sealed record BudgetReservation(Guid Id, Guid TenantId, string MonthKey, long ReservedMicroUsd);

public sealed record UsageRecord(
    string RequestId,
    Guid TenantId,
    string Model,
    string Provider,
    int PromptTokens,
    int CompletionTokens,
    long CostMicroUsd,
    long LatencyMs,
    int FallbackCount,
    int RedactionCount,
    string Status);

public sealed record DashboardSummary(
    long Requests24h,
    long Tokens24h,
    long CostMonthMicroUsd,
    long BudgetMicroUsd,
    double P95LatencyMs,
    long Fallbacks24h,
    IReadOnlyList<ProviderSummary> Providers);

public sealed record ProviderSummary(
    string Provider,
    long Requests,
    double AvgLatencyMs);
