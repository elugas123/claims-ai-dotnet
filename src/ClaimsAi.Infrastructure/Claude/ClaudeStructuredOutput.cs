using System.Diagnostics;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using ClaimsAi.Core;

namespace ClaimsAi.Infrastructure.Claude;

public sealed record ClaudeOptions
{
    public string Model { get; init; } = "claude-opus-5-5";
    public Effort Effort { get; init; } = Effort.Medium;
    public int MaxTokens { get; init; } = 4096;
}

/// <summary>One Claude call whose answer must match a JSON Schema (structured outputs), deserialized to T.</summary>
internal static class ClaudeStructuredOutput
{
    public static async Task<(T Value, long InputTokens, long OutputTokens, TimeSpan Latency)> CreateAsync<T>(
        AnthropicClient client,
        ClaudeOptions options,
        string systemPrompt,
        string userMessage,
        Dictionary<string, JsonElement> schema,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = options.MaxTokens,
            System = systemPrompt,
            OutputConfig = new OutputConfig
            {
                Effort = options.Effort,
                Format = new JsonOutputFormat { Schema = schema },
            },
            Messages = [new UserMessageParam { Content = userMessage }],
        }, cancellationToken);

        stopwatch.Stop();

        if (response.StopReason == "refusal")
        {
            throw new ClaimProcessingException($"The model declined this request: {response.StopDetails?.Explanation}");
        }
        if (response.StopReason == "max_tokens")
        {
            throw new ClaimProcessingException($"The answer was cut off at {options.MaxTokens} tokens.");
        }

        var json = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        return (ClaimsJson.Deserialize<T>(json), response.Usage.InputTokens, response.Usage.OutputTokens, stopwatch.Elapsed);
    }
}
