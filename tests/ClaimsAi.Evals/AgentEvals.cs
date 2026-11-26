using System.Globalization;
using System.Text;
using Anthropic;
using ClaimsAi.Core;
using ClaimsAi.Infrastructure.Claude;
using ClaimsAi.Infrastructure.Crm;
using ClaimsAi.Infrastructure.Policies;
using Xunit.Abstractions;

namespace ClaimsAi.Evals;

/// <summary>Evals of the triage agent: the next action it recommends and whether it spots fraud signals.</summary>
public class AgentEvals(ITestOutputHelper output)
{
    private static readonly JsonPolicyholderDirectory Crm =
        JsonPolicyholderDirectory.LoadFile(Path.Combine(EvalPaths.RepoRoot, "data", "crm", "policyholders.json"));

    private static readonly Bm25PolicyRetriever Retriever =
        new(MarkdownPolicyLoader.LoadDirectory(Path.Combine(EvalPaths.RepoRoot, "data", "policies")));

    private const double MinActionAccuracy = 0.75;

    /// <summary>Offline: every policy number in the eval set exists in the fake CRM, so lookups test the agent, not the data.</summary>
    [Fact]
    public void Crm_has_every_policy_in_the_eval_set()
    {
        var missing = EvalCase.LoadFile(EvalPaths.EvalSet)
            .Select(c => c.Expected.PolicyNumber)
            .OfType<string>()
            .Where(policy => Crm.Find(policy) is null)
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// Live: the agent investigates each labeled claim with real tool calls. It starts from the labeled extraction and
    /// coverage so earlier steps' errors don't leak into this score.
    /// </summary>
    [RequiresApiKeyFact]
    [Trait("Category", "LiveEval")]
    public async Task Agent_recommends_the_right_action_and_spots_fraud()
    {
        var agent = new ClaudeClaimAgent(new AnthropicClient(), new ClaimAgentTools(Crm, Retriever));
        var cases = EvalCase.LoadFile(EvalPaths.EvalSet);
        var report = new StringBuilder();
        int correctActions = 0, correctFraud = 0, toolCalls = 0;
        long inputTokens = 0, outputTokens = 0;

        foreach (var c in cases)
        {
            var coverage = new CoverageDecision { Verdict = c.ExpectedCoverage.Verdict, CitedClauses = c.ExpectedCoverage.Clauses };
            var result = await agent.TriageAsync(c.ToSubmission(), c.Expected, coverage);
            var triage = result.Triage;

            var actionOk = triage.RecommendedAction == c.ExpectedTriage.Action;
            var fraudOk = triage.FraudSignals.Count > 0 == c.ExpectedTriage.Fraud;
            correctActions += actionOk ? 1 : 0;
            correctFraud += fraudOk ? 1 : 0;
            toolCalls += result.ToolCalls.Count;
            inputTokens += result.InputTokens;
            outputTokens += result.OutputTokens;

            report.AppendLine(CultureInfo.InvariantCulture,
                $"  {c.Id}  {(actionOk && fraudOk ? "PASS" : "FAIL")}  {triage.RecommendedAction} (expected {c.ExpectedTriage.Action})" +
                $"  fraud signals: {triage.FraudSignals.Count}  tools: {string.Join(", ", result.ToolCalls.Select(t => t.Name))}");
            if (!actionOk || !fraudOk)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"          {triage.InternalNotes}");
            }
        }

        var actionAccuracy = (double)correctActions / cases.Count;
        var fraudAccuracy = (double)correctFraud / cases.Count;
        var cost = ModelPricing.ClaudeOpus55.Cost(inputTokens, outputTokens);
        report.AppendLine(CultureInfo.InvariantCulture,
            $"Action accuracy {actionAccuracy:P1} · fraud detection {fraudAccuracy:P1} · {toolCalls} tool calls · ${cost:0.0000}");
        output.WriteLine(report.ToString());

        Assert.True(actionAccuracy >= MinActionAccuracy, $"Action accuracy {actionAccuracy:P1} is below {MinActionAccuracy:P0}.\n{report}");
    }
}
