using System.Globalization;
using System.Text;
using System.Text.Json;
using ClaimsAi.Core;

namespace ClaimsAi.Evals;

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

    /// <summary>Human-readable report: each case with its misses, accuracy per field, cost and latency.</summary>
    public string ToText()
    {
        var text = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        text.AppendLine(inv, $"Eval · {Model} · effort {Effort} · {Cases.Count} cases");
        text.AppendLine();

        foreach (var c in Cases)
        {
            if (c.Error is not null)
            {
                text.AppendLine(inv, $"  {c.CaseId}  ERROR  {c.Error}");
                continue;
            }
            var status = c.Score!.Perfect ? "PASS" : "FAIL";
            text.AppendLine(inv, $"  {c.CaseId}  {status}  {c.Score.Matches,2}/{c.Score.Fields.Count}  {c.Extraction!.Latency.TotalSeconds,5:0.0}s");
            foreach (var miss in c.Score.Fields.Where(f => !f.Match))
            {
                text.AppendLine(inv, $"          {miss.Field}: expected {miss.Expected ?? "null"}, got {miss.Actual ?? "null"}");
            }
        }

        text.AppendLine();
        text.AppendLine("Field accuracy");
        foreach (var f in FieldAccuracies)
        {
            text.AppendLine(inv, $"  {f.Field,-22} {f.Accuracy * 100,6:0.0}%  {f.Matches}/{f.Total}");
        }

        text.AppendLine();
        text.AppendLine(inv, $"Overall   {OverallAccuracy * 100:0.0}% of fields · {PerfectCases}/{Cases.Count} cases perfect · {Errors} errors");
        text.AppendLine(inv, $"Tokens    {InputTokens:N0} in · {OutputTokens:N0} out · ${CostUsd:0.0000}");
        text.AppendLine(inv, $"Latency   p50 {LatencyP50.TotalSeconds:0.0}s · p95 {LatencyP95.TotalSeconds:0.0}s");
        return text.ToString();
    }

    /// <summary>Saves the report as JSON so runs can be compared over time. Returns the file path.</summary>
    public string SaveTo(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{RanAt:yyyyMMdd-HHmmss}-{Model}-{Effort}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(this, ClaimExtractionJson.Options));
        return path;
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
        CancellationToken cancellationToken = default)
    {
        var ranAt = DateTimeOffset.UtcNow;
        using var gate = new SemaphoreSlim(maxConcurrency);

        var tasks = cases.Select(async evalCase =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await RunCaseAsync(evalCase, extractor, cancellationToken);
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
