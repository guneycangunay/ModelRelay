using System.Collections.Concurrent;

namespace ModelRelay.Core.Routing;

public sealed class ProviderCircuitRegistry
{
    private sealed record CircuitState(int ConsecutiveFailures, DateTimeOffset? OpenUntil);

    private readonly ConcurrentDictionary<string, CircuitState> _states = new(StringComparer.OrdinalIgnoreCase);

    public bool IsOpen(string provider, DateTimeOffset now)
    {
        if (!_states.TryGetValue(provider, out var state) || state.OpenUntil is null)
        {
            return false;
        }

        if (state.OpenUntil > now)
        {
            return true;
        }

        _states.TryRemove(provider, out _);
        return false;
    }

    public void ReportSuccess(string provider) => _states.TryRemove(provider, out _);

    public void ReportFailure(string provider, int failureThreshold, TimeSpan openDuration, DateTimeOffset now)
    {
        _states.AddOrUpdate(
            provider,
            _ => new CircuitState(1, failureThreshold <= 1 ? now.Add(openDuration) : null),
            (_, current) =>
            {
                var failures = current.ConsecutiveFailures + 1;
                return new CircuitState(
                    failures,
                    failures >= failureThreshold ? now.Add(openDuration) : current.OpenUntil);
            });
    }
}
