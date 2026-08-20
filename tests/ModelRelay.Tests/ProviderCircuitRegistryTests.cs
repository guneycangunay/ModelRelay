using ModelRelay.Core.Routing;
using Xunit;

namespace ModelRelay.Tests;

public sealed class ProviderCircuitRegistryTests
{
    [Fact]
    public void Circuit_OpensAtThreshold_AndClosesAfterWindow()
    {
        var circuits = new ProviderCircuitRegistry();
        var now = new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);

        circuits.ReportFailure("provider-a", 2, TimeSpan.FromSeconds(20), now);
        Assert.False(circuits.IsOpen("provider-a", now));

        circuits.ReportFailure("provider-a", 2, TimeSpan.FromSeconds(20), now);
        Assert.True(circuits.IsOpen("provider-a", now.AddSeconds(19)));
        Assert.False(circuits.IsOpen("provider-a", now.AddSeconds(21)));
    }

    [Fact]
    public void Success_ResetsFailureState()
    {
        var circuits = new ProviderCircuitRegistry();
        var now = DateTimeOffset.UtcNow;

        circuits.ReportFailure("provider-a", 1, TimeSpan.FromMinutes(1), now);
        Assert.True(circuits.IsOpen("provider-a", now));

        circuits.ReportSuccess("provider-a");
        Assert.False(circuits.IsOpen("provider-a", now));
    }
}
