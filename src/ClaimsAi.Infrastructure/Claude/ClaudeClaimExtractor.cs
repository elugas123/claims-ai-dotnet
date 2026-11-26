using Anthropic;
using Anthropic.Models.Messages;
using ClaimsAi.Core;

namespace ClaimsAi.Infrastructure.Claude;

/// <summary>Extracts structured claim data with a single Claude call constrained by a JSON Schema.</summary>
public sealed class ClaudeClaimExtractor(AnthropicClient client, ClaudeOptions? options = null) : IClaimExtractor
{
    // Extraction is a simple task, so low effort keeps cost and latency down. Raise it if evals show errors.
    private readonly ClaudeOptions _options = options ?? new() { Effort = Effort.Low };

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
        var userMessage = $"Received date: {submission.ReceivedAt:yyyy-MM-dd}\n\n<claim_report>\n{submission.Text}\n</claim_report>";

        var result = await ClaudeStructuredOutput.CreateAsync<ClaimExtraction>(
            client, _options, SystemPrompt, userMessage, ClaimsJson.ExtractionSchema, cancellationToken);

        return new ExtractionResult(result.Value, result.InputTokens, result.OutputTokens, result.Latency);
    }
}
