using Xunit;
using ModelRelay.Core.Billing;

namespace ModelRelay.Tests;

public sealed class ModelPricingTests
{
    [Theory]
    [InlineData("relay-fast", 100, 50, 200)]
    [InlineData("relay-balanced", 100, 50, 400)]
    public void CalculateActualCost_UsesIntegerMicroUsd(
        string model,
        int promptTokens,
        int completionTokens,
        long expected)
    {
        var actual = ModelPricing.CalculateActualCost(model, promptTokens, completionTokens);
        Assert.Equal(expected, actual);
    }
}
