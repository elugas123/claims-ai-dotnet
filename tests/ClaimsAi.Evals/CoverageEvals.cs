using System.Globalization;
using System.Text;
using Anthropic;
using ClaimsAi.Core;
using ClaimsAi.Infrastructure.Claude;
using ClaimsAi.Infrastructure.Policies;
using Xunit.Abstractions;

namespace ClaimsAi.Evals;

/// <summary>
/// RAG evals, split in its two halves so a failure points at the right part:
/// retrieval (did we find the right clauses?) and generation (did the model decide and cite correctly?).
/// </summary>
public class CoverageEvals(ITestOutputHelper output)
{
    private static readonly IReadOnlyList<PolicyClause> Clauses =
        MarkdownPolicyLoader.LoadDirectory(Path.Combine(EvalPaths.RepoRoot, "data", "policies"));

    private static readonly Bm25PolicyRetriever Retriever = new(Clauses);

    // Retrieval runs offline and is deterministic, so it can hold a high bar.
    private const double MinRetrievalRecall = 0.9;

    // Generation varies a little between runs; gate on the overall score.
    private const double MinVerdictAccuracy = 0.8;

    /// <summary>
    /// Offline, no API: for every labeled claim, are the clauses a correct answer needs among the retrieved ones?
    /// Uses the labeled extraction without a summary, i.e. the worst case for the query.
    /// </summary>
    [Fact]
    public void Retrieval_finds_the_clauses_each_claim_needs()
    {
        var report = new StringBuilder();
        var found = 0;
        var needed = 0;

        foreach (var c in EvalCase.LoadFile(EvalPaths.EvalSet))
        {
            var retrieved = ClaimAssessmentService.RetrieveClauses(Retriever, c.ToSubmission(), c.Expected)
                .Select(r => r.Id).ToHashSet();
            var missing = c.ExpectedCoverage.Clauses.Where(id => !retrieved.Contains(id)).ToList();

            found += c.ExpectedCoverage.Clauses.Count - missing.Count;
            needed += c.ExpectedCoverage.Clauses.Count;
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  {c.Id}  {(missing.Count == 0 ? "OK  " : "MISS")}  missing: {(missing.Count == 0 ? "-" : string.Join(", ", missing))}");
        }

        var recall = (double)found / needed;
        report.AppendLine(CultureInfo.InvariantCulture, $"Retrieval recall@{ClaimAssessmentService.ClausesToRetrieve}: {recall:P1} ({found}/{needed})");
        output.WriteLine(report.ToString());

        Assert.True(recall >= MinRetrievalRecall, $"Retrieval recall {recall:P1} is below {MinRetrievalRecall:P0}.\n{report}");
    }

    /// <summary>
    /// Live: retrieval + Claude coverage decision for every labeled claim. Starts from the labeled extraction so
    /// extraction errors don't leak into this score.
    /// </summary>
    [RequiresApiKeyFact]
    [Trait("Category", "LiveEval")]
    public async Task Coverage_verdicts_and_citations_match_the_labels()
    {
        var checker = new ClaudeCoverageChecker(new AnthropicClient());
        var report = new StringBuilder();
        var cases = EvalCase.LoadFile(EvalPaths.EvalSet);
        var correctVerdicts = 0;
        var citedNeeded = 0;
        var needed = 0;
        long inputTokens = 0, outputTokens = 0;

        foreach (var c in cases)
        {
            var clauses = ClaimAssessmentService.RetrieveClauses(Retriever, c.ToSubmission(), c.Expected);
            var result = await checker.CheckAsync(c.ToSubmission(), c.Expected, clauses);
            var decision = result.Decision;

            var verdictOk = decision.Verdict == c.ExpectedCoverage.Verdict;
            var cited = c.ExpectedCoverage.Clauses.Count(decision.CitedClauses.Contains);
            correctVerdicts += verdictOk ? 1 : 0;
            citedNeeded += cited;
            needed += c.ExpectedCoverage.Clauses.Count;
            inputTokens += result.InputTokens;
            outputTokens += result.OutputTokens;

            report.AppendLine(CultureInfo.InvariantCulture,
                $"  {c.Id}  {(verdictOk ? "PASS" : "FAIL")}  verdict {decision.Verdict} (expected {c.ExpectedCoverage.Verdict})" +
                $"  cited {cited}/{c.ExpectedCoverage.Clauses.Count}: {string.Join(", ", decision.CitedClauses)}");
            if (!verdictOk)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"          {decision.Reasoning}");
            }
        }

        var verdictAccuracy = (double)correctVerdicts / cases.Count;
        var citationRecall = (double)citedNeeded / needed;
        var cost = ModelPricing.ClaudeOpus55.Cost(inputTokens, outputTokens);
        report.AppendLine(CultureInfo.InvariantCulture, $"Verdict accuracy {verdictAccuracy:P1} · citation recall {citationRecall:P1} · ${cost:0.0000}");
        output.WriteLine(report.ToString());

        Assert.True(verdictAccuracy >= MinVerdictAccuracy, $"Verdict accuracy {verdictAccuracy:P1} is below {MinVerdictAccuracy:P0}.\n{report}");
    }
}
