using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ModelRelay.Core.Abstractions;
using ModelRelay.Core.Models;
using ModelRelay.Core.Routing;

namespace ModelRelay.Infrastructure.Providers;

public sealed class FakeLlmProvider : ILlmProvider
{
    private readonly string _flavor;

    public FakeLlmProvider(string name, string flavor)
    {
        Name = name;
        _flavor = flavor;
    }

    public string Name { get; }

    public async Task<ProviderResult> CompleteAsync(ProviderRequest request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var prompt = string.Join("\n", request.Messages.Select(x => $"{x.Role}:{x.Content}"));

        if (prompt.Contains("[fail-all]", StringComparison.OrdinalIgnoreCase))
        {
            throw new ProviderTransientException($"{Name} simulated a transient outage.");
        }

        if (Name.Equals("fake-primary", StringComparison.OrdinalIgnoreCase)
            && prompt.Contains("[fail-primary]", StringComparison.OrdinalIgnoreCase))
        {
            throw new ProviderTransientException("Primary provider simulated a transient outage.");
        }

        if (prompt.Contains("[timeout]", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
        else
        {
            await Task.Delay(Name.Equals("fake-primary", StringComparison.OrdinalIgnoreCase) ? 55 : 90, cancellationToken);
        }

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt))).ToLowerInvariant()[..8];
        var lastUser = request.Messages
            .Where(x => x.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Content)
            .LastOrDefault() ?? "No user message supplied.";

        var content = $"[{_flavor}:{digest}] {BuildAnswer(lastUser, request.MaxTokens)}";
        var promptTokens = EstimateTokens(prompt);
        var completionTokens = Math.Min(request.MaxTokens, EstimateTokens(content));

        sw.Stop();
        return new ProviderResult(Name, content, promptTokens, completionTokens, sw.ElapsedMilliseconds);
    }

    private static string BuildAnswer(string input, int maxTokens)
    {
        var normalized = string.Join(' ', input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var answer = $"ModelRelay sandbox response. Your request was routed safely and deterministically. Input summary: {normalized}";
        var maxChars = Math.Max(32, maxTokens * 4);
        return answer.Length <= maxChars ? answer : answer[..maxChars];
    }

    private static int EstimateTokens(string text) => Math.Max(1, (int)Math.Ceiling(text.Length / 4d));
}
