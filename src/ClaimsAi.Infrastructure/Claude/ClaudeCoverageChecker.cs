using System.Text;
using Anthropic;
using ClaimsAi.Core;

namespace ClaimsAi.Infrastructure.Claude;

/// <summary>
/// Decides coverage with Claude, grounded only on the retrieved clauses (the "generation" half of RAG).
/// Citations the model makes up are dropped: only ids of clauses it was given survive.
/// </summary>
public sealed class ClaudeCoverageChecker(AnthropicClient client, ClaudeOptions? options = null) : ICoverageChecker
{
    // Coverage is a judgment call over several clauses, so it gets more effort than extraction.
    private readonly ClaudeOptions _options = options ?? new();

    internal const string SystemPrompt = """
        You are a claims adjuster assistant for Contoso Insurance, a fictional insurer.
        Decide whether a claim is covered using ONLY the policy clauses provided. Do not rely on outside knowledge
        of insurance; if the clauses don't settle it, say so with "needs_review".

        Verdict:
        - covered: a covered-event clause applies and no exclusion applies.
        - not_covered: an exclusion applies, or no covered-event clause matches what happened.
        - needs_review: coverage depends on information that is missing or on a condition that may not be met
          (for example a police report required within a deadline that hasn't been filed).

        cited_clauses: the ids exactly as written in brackets, e.g. "AUTO-COMP-2026#1.1". Cite the clause that drives
        the verdict (covered event or exclusion) and the deductible clause when the claim is covered.
        deductible: the deductible that applies, as stated in the clauses (e.g. "USD 500 per event"), or null.
        required_documents: documents the reporting obligations still require for this claim.
        reasoning: two or three sentences in English.
        """;

    public async Task<CoverageResult> CheckAsync(
        ClaimSubmission submission,
        ClaimExtraction claim,
        IReadOnlyList<PolicyClause> clauses,
        CancellationToken cancellationToken = default)
    {
        var result = await ClaudeStructuredOutput.CreateAsync<CoverageDecision>(
            client, _options, SystemPrompt, BuildUserMessage(submission, claim, clauses), ClaimsJson.CoverageSchema, cancellationToken);

        var givenIds = clauses.Select(c => c.Id).ToHashSet();
        var decision = result.Value with { CitedClauses = result.Value.CitedClauses.Where(givenIds.Contains).ToList() };

        return new CoverageResult(decision, result.InputTokens, result.OutputTokens, result.Latency);
    }

    internal static string BuildUserMessage(ClaimSubmission submission, ClaimExtraction claim, IReadOnlyList<PolicyClause> clauses)
    {
        var text = new StringBuilder();
        text.AppendLine($"Received date: {submission.ReceivedAt:yyyy-MM-dd}");
        text.AppendLine();
        text.AppendLine("<policy_clauses>");
        foreach (var clause in clauses)
        {
            text.AppendLine($"[{clause.Id}] ({clause.Section}) {clause.Text}");
        }
        text.AppendLine("</policy_clauses>");
        text.AppendLine();
        text.AppendLine("<claim_data>");
        text.AppendLine(ClaimsJson.Serialize(claim));
        text.AppendLine("</claim_data>");
        text.AppendLine();
        text.AppendLine("<claim_report>");
        text.AppendLine(submission.Text);
        text.AppendLine("</claim_report>");
        return text.ToString();
    }
}
