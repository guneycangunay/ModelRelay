using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ModelRelay.Api.Telemetry;

public static class RelayTelemetry
{
    public const string SourceName = "ModelRelay";
    public static readonly ActivitySource ActivitySource = new(SourceName);
    public static readonly Meter Meter = new(SourceName, "1.0.0");

    public static readonly Counter<long> Requests = Meter.CreateCounter<long>("modelrelay.requests");
    public static readonly Counter<long> Fallbacks = Meter.CreateCounter<long>("modelrelay.fallbacks");
    public static readonly Counter<long> Redactions = Meter.CreateCounter<long>("modelrelay.redactions");
    public static readonly Histogram<double> ProviderLatency = Meter.CreateHistogram<double>("modelrelay.provider.latency", "ms");
}
