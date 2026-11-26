using ClaimsAi.Core.Extraction;

namespace ClaimsAi.Core.Evals;

/// <summary>Price per million tokens, in USD.</summary>
public sealed record ModelPricing(decimal InputPerMillion, decimal OutputPerMillion)
{
    public static readonly ModelPricing ClaudeOpus55 = new(4m, 20m);

    public decimal Cost(long inputTokens, long outputTokens) =>
        (inputTokens * InputPerMillion + outputTokens * OutputPerMillion) / 1_000_000m;
}

/// <summary>Outcome of one eval case. Score is null when the extractor threw.</summary>
public sealed record CaseResult(string CaseId, CaseScore? Score, ExtractionResult? Extraction, string? Error)
{
    public int Matches => Score?.Matches ?? 0;
}

public sealed record FieldAccuracy(string Field, int Matches, int Total)
{
    public double Accuracy => Total == 0 ? 0 : (double)Matches / Total;
}

public sealed record EvalReport(
    string Model,
    string Effort,
    DateTimeOffset RanAt,
    IReadOnlyList<CaseResult> Cases,
    ModelPricing Pricing)
{
    /// <summary>Every case is scored on the same fields; a case that errored counts as missing all of them.</summary>
    public static readonly IReadOnlyList<string> ScoredFields =
    [
        "claim_type", "incident_date", "location", "policy_number", "vehicle_plate",
        "estimated_amount", "currency", "injuries_reported", "police_report_filed", "third_party_involved",
    ];

    public IReadOnlyList<FieldAccuracy> FieldAccuracies => ScoredFields
        .Select(name => new FieldAccuracy(
            name,
            Cases.Count(c => c.Score?.Fields.Any(f => f.Field == name && f.Match) == true),
            Cases.Count))
        .ToList();

    public double OverallAccuracy =>
        Cases.Count == 0 ? 0 : (double)Cases.Sum(c => c.Matches) / (Cases.Count * ScoredFields.Count);

    public int PerfectCases => Cases.Count(c => c.Score?.Perfect == true);
    public int Errors => Cases.Count(c => c.Error is not null);

    public long InputTokens => Cases.Sum(c => c.Extraction?.InputTokens ?? 0);
    public long OutputTokens => Cases.Sum(c => c.Extraction?.OutputTokens ?? 0);
    public decimal CostUsd => Pricing.Cost(InputTokens, OutputTokens);

    public TimeSpan LatencyP50 => Percentile(0.50);
    public TimeSpan LatencyP95 => Percentile(0.95);

    private TimeSpan Percentile(double p)
    {
        var latencies = Cases.Where(c => c.Extraction is not null).Select(c => c.Extraction!.Latency).Order().ToList();
        if (latencies.Count == 0)
        {
            return TimeSpan.Zero;
        }
        var index = (int)Math.Ceiling(p * latencies.Count) - 1;
        return latencies[Math.Clamp(index, 0, latencies.Count - 1)];
    }
}

public static class EvalRunner
{
    public static async Task<EvalReport> RunAsync(
        IReadOnlyList<EvalCase> cases,
        IClaimExtractor extractor,
        string model,
        string effort,
        ModelPricing pricing,
        int maxConcurrency = 4,
        IProgress<CaseResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var ranAt = DateTimeOffset.UtcNow;
        using var gate = new SemaphoreSlim(maxConcurrency);

        var tasks = cases.Select(async evalCase =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var result = await RunCaseAsync(evalCase, extractor, cancellationToken);
                progress?.Report(result);
                return result;
            }
            finally
            {
                gate.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return new EvalReport(model, effort, ranAt, results.OrderBy(r => r.CaseId).ToList(), pricing);
    }

    private static async Task<CaseResult> RunCaseAsync(EvalCase evalCase, IClaimExtractor extractor, CancellationToken ct)
    {
        try
        {
            var extraction = await extractor.ExtractAsync(evalCase.ToSubmission(), ct);
            var score = ExtractionScorer.Score(evalCase.Id, evalCase.Expected, extraction.Extraction);
            return new CaseResult(evalCase.Id, score, extraction, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CaseResult(evalCase.Id, null, null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
