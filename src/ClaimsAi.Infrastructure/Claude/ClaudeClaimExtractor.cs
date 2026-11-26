using System.Diagnostics;
using Anthropic;
using Anthropic.Models.Messages;
using ClaimsAi.Core;

namespace ClaimsAi.Infrastructure.Claude;

public sealed record ClaudeExtractorOptions
{
    public string Model { get; init; } = "claude-opus-5-5";

    /// <summary>Extraction is a simple task, so low effort keeps cost and latency down. Raise it if evals show errors.</summary>
    public Effort Effort { get; init; } = Effort.Low;

    public int MaxTokens { get; init; } = 4096;
}

/// <summary>Extracts structured claim data with a single Claude call constrained by a JSON Schema.</summary>
public sealed class ClaudeClaimExtractor(AnthropicClient client, ClaudeExtractorOptions? options = null) : IClaimExtractor
{
    private readonly ClaudeExtractorOptions _options = options ?? new();

    internal const string SystemPrompt = """
        You are a claims intake assistant for Contoso Insurance, a fictional insurer.
        You receive a customer's claim report, written in any language, and extract structured data from it.

        Rules:
        - Only use information stated in the report. Never invent a value; use null when it is not stated.
        - incident_date: resolve relative dates ("yesterday", "last night", "on Saturday") against the received date.
          If the report gives a range or says the date is unknown, use null.
        - estimated_amount: a plain number (1.200.000 pesos -> 1200000). currency: ISO 4217 code inferred from the text
          ("dollars"/"$" -> USD, "pesos" in Argentina -> ARS). Both null if no amount is given.
        - claim_type: pick the closest category. Mechanical failures, wear and tear and anything that isn't a
          covered event type go to "other".
        - third_party_involved: true when another person or vehicle caused or was affected by the incident,
          including an unknown driver who hit a parked car.
        - summary: one sentence in English.
        - missing_information: what the report still needs according to standard requirements: date, location,
          policy number, plate (auto), police report number (theft, burglary, injuries), damage estimate.
          Write each item in English.
        """;

    public async Task<ExtractionResult> ExtractAsync(ClaimSubmission submission, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            System = SystemPrompt,
            OutputConfig = new OutputConfig
            {
                Effort = _options.Effort,
                Format = new JsonOutputFormat { Schema = ClaimExtractionJson.Schema },
            },
            Messages =
            [
                new UserMessageParam
                {
                    Content = $"Received date: {submission.ReceivedAt:yyyy-MM-dd}\n\n<claim_report>\n{submission.Text}\n</claim_report>",
                },
            ],
        }, cancellationToken);

        stopwatch.Stop();

        if (response.StopReason == "refusal")
        {
            throw new ClaimExtractionException($"The model declined to process this claim: {response.StopDetails?.Explanation}");
        }
        if (response.StopReason == "max_tokens")
        {
            throw new ClaimExtractionException($"Extraction was cut off at {_options.MaxTokens} tokens.");
        }

        var json = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        var extraction = ClaimExtractionJson.Deserialize(json);

        return new ExtractionResult(extraction, response.Usage.InputTokens, response.Usage.OutputTokens, stopwatch.Elapsed);
    }
}
