using System.Diagnostics;
using ModelRelay.Core.Abstractions;
using ModelRelay.Core.Models;

namespace ModelRelay.Core.Routing;

public sealed record RouterPolicy(
    int TimeoutMilliseconds = 1800,
    int MaxRetries = 1,
    int CircuitFailureThreshold = 3,
    int CircuitOpenSeconds = 20);

public sealed class ProviderTransientException : Exception
{
    public ProviderTransientException(string message) : base(message) { }
}

public sealed class AllProvidersUnavailableException : Exception
{
    public AllProvidersUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class ModelRouter
{
    private readonly IReadOnlyDictionary<string, ILlmProvider> _providers;
    private readonly ProviderCircuitRegistry _circuits;
    private readonly RouterPolicy _policy;
    private readonly TimeProvider _timeProvider;

    public ModelRouter(
        IEnumerable<ILlmProvider> providers,
        ProviderCircuitRegistry circuits,
        RouterPolicy policy,
        TimeProvider? timeProvider = null)
    {
        _providers = providers.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        _circuits = circuits;
        _policy = policy;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<(ProviderResult Result, int FallbackCount)> CompleteAsync(
        ProviderRequest request,
        CancellationToken cancellationToken)
    {
        var providerOrder = request.Model.ToLowerInvariant() switch
        {
            "relay-fast" => new[] { "fake-primary", "fake-secondary" },
            "relay-balanced" => new[] { "fake-secondary", "fake-primary" },
            _ => throw new ArgumentException($"Unsupported model '{request.Model}'.", nameof(request))
        };

        Exception? lastError = null;
        var fallbackCount = 0;

        foreach (var providerName in providerOrder)
        {
            if (!_providers.TryGetValue(providerName, out var provider))
            {
                fallbackCount++;
                continue;
            }

            if (_circuits.IsOpen(providerName, _timeProvider.GetUtcNow()))
            {
                fallbackCount++;
                continue;
            }

            for (var attempt = 0; attempt <= _policy.MaxRetries; attempt++)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMilliseconds(_policy.TimeoutMilliseconds));

                try
                {
                    var result = await provider.CompleteAsync(request, timeout.Token);
                    _circuits.ReportSuccess(providerName);
                    return (result, fallbackCount);
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    lastError = ex;
                    _circuits.ReportFailure(
                        providerName,
                        _policy.CircuitFailureThreshold,
                        TimeSpan.FromSeconds(_policy.CircuitOpenSeconds),
                        _timeProvider.GetUtcNow());
                }
                catch (ProviderTransientException ex)
                {
                    lastError = ex;
                    _circuits.ReportFailure(
                        providerName,
                        _policy.CircuitFailureThreshold,
                        TimeSpan.FromSeconds(_policy.CircuitOpenSeconds),
                        _timeProvider.GetUtcNow());
                }
            }

            fallbackCount++;
        }

        throw new AllProvidersUnavailableException("No provider completed the request within the configured resilience policy.", lastError);
    }
}
