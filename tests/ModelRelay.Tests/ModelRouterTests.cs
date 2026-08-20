using Xunit;
using ModelRelay.Core.Models;
using ModelRelay.Core.Routing;
using ModelRelay.Infrastructure.Providers;

namespace ModelRelay.Tests;

public sealed class ModelRouterTests
{
    [Fact]
    public async Task CompleteAsync_FallsBack_WhenPrimaryFails()
    {
        var router = new ModelRouter(
            new[]
            {
                new FakeLlmProvider("fake-primary", "primary"),
                new FakeLlmProvider("fake-secondary", "secondary")
            },
            new ProviderCircuitRegistry(),
            new RouterPolicy(500, 0, 3, 20));

        var result = await router.CompleteAsync(
            new ProviderRequest(
                "req_1",
                "relay-fast",
                new[] { ("user", "[fail-primary] explain idempotency") },
                128,
                0.2),
            CancellationToken.None);

        Assert.Equal("fake-secondary", result.Result.Provider);
        Assert.Equal(1, result.FallbackCount);
    }
}
