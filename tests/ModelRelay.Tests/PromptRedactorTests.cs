using Xunit;
using ModelRelay.Core.Contracts;
using ModelRelay.Core.Security;

namespace ModelRelay.Tests;

public sealed class PromptRedactorTests
{
    [Fact]
    public void Redact_ReplacesSensitivePatterns_AndCountsThem()
    {
        var input = new[]
        {
            new ChatMessage("user", "Email me at jane@example.com and use sk-secret_123456789012345.")
        };

        var (messages, count) = PromptRedactor.Redact(input);

        Assert.Equal(2, count);
        Assert.DoesNotContain("jane@example.com", messages[0].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[REDACTED_EMAIL]", messages[0].Content);
        Assert.Contains("[REDACTED_SECRET]", messages[0].Content);
    }
}
