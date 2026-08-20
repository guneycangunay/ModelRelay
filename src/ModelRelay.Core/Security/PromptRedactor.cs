using System.Text.RegularExpressions;
using ModelRelay.Core.Contracts;

namespace ModelRelay.Core.Security;

public static partial class PromptRedactor
{
    public static (IReadOnlyList<ChatMessage> Messages, int RedactionCount) Redact(IReadOnlyList<ChatMessage> messages)
    {
        var count = 0;
        var output = new List<ChatMessage>(messages.Count);

        foreach (var message in messages)
        {
            var content = message.Content;
            content = Replace(EmailRegex(), content, "[REDACTED_EMAIL]", ref count);
            content = Replace(BearerRegex(), content, "[REDACTED_SECRET]", ref count);
            content = Replace(CardLikeRegex(), content, "[REDACTED_NUMBER]", ref count);
            output.Add(message with { Content = content });
        }

        return (output, count);
    }

    private static string Replace(Regex regex, string input, string replacement, ref int count)
    {
        var matches = regex.Matches(input).Count;
        if (matches == 0)
        {
            return input;
        }

        count += matches;
        return regex.Replace(input, replacement);
    }

    [GeneratedRegex(@"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b(?:sk|api|token|bearer)[-_ ]?[A-Za-z0-9_\-]{12,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"\b(?:\d[ -]*?){13,19}\b", RegexOptions.CultureInvariant)]
    private static partial Regex CardLikeRegex();
}
