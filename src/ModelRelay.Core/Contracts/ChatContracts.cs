using System.Text.Json.Serialization;

namespace ModelRelay.Core.Contracts;

public sealed record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

public sealed record ChatCompletionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
    [property: JsonPropertyName("stream")] bool Stream = false,
    [property: JsonPropertyName("max_tokens")] int? MaxTokens = null,
    [property: JsonPropertyName("temperature")] double? Temperature = null);

public sealed record ChatUsage(
    [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
    [property: JsonPropertyName("completion_tokens")] int CompletionTokens,
    [property: JsonPropertyName("total_tokens")] int TotalTokens);

public sealed record ChatChoice(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("message")] ChatMessage Message,
    [property: JsonPropertyName("finish_reason")] string FinishReason);

public sealed record ChatCompletionResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice> Choices,
    [property: JsonPropertyName("usage")] ChatUsage Usage);
