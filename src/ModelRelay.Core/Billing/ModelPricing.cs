namespace ModelRelay.Core.Billing;

public sealed record ModelPrice(long InputMicroUsdPerToken, long OutputMicroUsdPerToken);

public static class ModelPricing
{
    // Synthetic sandbox pricing. These are not vendor prices.
    private static readonly IReadOnlyDictionary<string, ModelPrice> Prices =
        new Dictionary<string, ModelPrice>(StringComparer.OrdinalIgnoreCase)
        {
            ["relay-fast"] = new(1, 2),
            ["relay-balanced"] = new(2, 4)
        };

    public static bool TryGet(string model, out ModelPrice price) =>
        Prices.TryGetValue(model, out price!);

    public static long EstimateMaximumCost(string model, int promptTokens, int maxCompletionTokens)
    {
        if (!TryGet(model, out var price))
        {
            throw new ArgumentException($"Unsupported model '{model}'.", nameof(model));
        }

        return checked((long)promptTokens * price.InputMicroUsdPerToken
            + (long)maxCompletionTokens * price.OutputMicroUsdPerToken);
    }

    public static long CalculateActualCost(string model, int promptTokens, int completionTokens)
    {
        if (!TryGet(model, out var price))
        {
            throw new ArgumentException($"Unsupported model '{model}'.", nameof(model));
        }

        return checked((long)promptTokens * price.InputMicroUsdPerToken
            + (long)completionTokens * price.OutputMicroUsdPerToken);
    }
}
