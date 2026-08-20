using ModelRelay.Core.Contracts;

namespace ModelRelay.Core.Tokens;

public static class TokenEstimator
{
    public static int Estimate(IReadOnlyList<ChatMessage> messages)
    {
        var chars = messages.Sum(x => x.Content.Length + x.Role.Length + 4);
        return Math.Max(1, (int)Math.Ceiling(chars / 4d));
    }
}
