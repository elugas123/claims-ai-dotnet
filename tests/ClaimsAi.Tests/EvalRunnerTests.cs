using ClaimsAi.Core.Evals;
using ClaimsAi.Core.Extraction;

namespace ClaimsAi.Tests;

public class EvalRunnerTests
{
    private static readonly ClaimExtraction Label = new()
    {
        ClaimType = ClaimType.AutoGlass,
        IncidentDate = "2026-11-07",
        Location = "Highway 9",
        PolicyNumber = null,
        VehiclePlate = "QWE-1180",
        EstimatedAmount = 450,
        Currency = "USD",
        InjuriesReported = false,
        PoliceReportFiled = false,
        ThirdPartyInvolved = false,
    };

    private static readonly IReadOnlyList<EvalCase> Cases =
    [
        new("A", new DateOnly(2026, 11, 7), "perfect", Label),
        new("B", new DateOnly(2026, 11, 7), "wrong date", Label),
        new("C", new DateOnly(2026, 11, 7), "throws", Label),
    ];

    /// <summary>A: returns the label. B: gets the date wrong. C: throws.</summary>
    private sealed class ScriptedExtractor : IClaimExtractor
    {
        public Task<ExtractionResult> ExtractAsync(ClaimSubmission submission, CancellationToken cancellationToken = default) =>
            submission.Text switch
            {
                "perfect" => Task.FromResult(new ExtractionResult(Label, 1000, 200, TimeSpan.FromSeconds(1))),
                "wrong date" => Task.FromResult(new ExtractionResult(
                    Label with { IncidentDate = "2026-11-06" }, 3000, 400, TimeSpan.FromSeconds(3))),
                _ => throw new ClaimExtractionException("boom"),
            };
    }

    private static Task<EvalReport> Run() =>
        EvalRunner.RunAsync(Cases, new ScriptedExtractor(), "test-model", "low", ModelPricing.ClaudeOpus55, maxConcurrency: 2);

    [Fact]
    public async Task Errors_are_recorded_and_count_as_misses()
    {
        var report = await Run();

        Assert.Equal(1, report.Errors);
        Assert.Contains("boom", report.Cases.Single(c => c.CaseId == "C").Error);
        Assert.Equal(1, report.PerfectCases);
        // 10 + 9 + 0 matches out of 3 cases x 10 fields
        Assert.Equal(19.0 / 30, report.OverallAccuracy, precision: 6);
    }

    [Fact]
    public async Task Field_accuracy_points_at_the_failing_field()
    {
        var report = await Run();
        var byField = report.FieldAccuracies.ToDictionary(f => f.Field);

        Assert.Equal(1, byField["incident_date"].Matches);
        Assert.Equal(2, byField["claim_type"].Matches);
        Assert.All(byField.Values, f => Assert.Equal(3, f.Total));
    }

    [Fact]
    public async Task Tokens_cost_and_latency_add_up()
    {
        var report = await Run();

        Assert.Equal(4000, report.InputTokens);
        Assert.Equal(600, report.OutputTokens);
        // 4000 * $4/M + 600 * $20/M = $0.016 + $0.012
        Assert.Equal(0.028m, report.CostUsd);
        Assert.Equal(TimeSpan.FromSeconds(1), report.LatencyP50);
        Assert.Equal(TimeSpan.FromSeconds(3), report.LatencyP95);
    }

    [Fact]
    public async Task Results_are_sorted_by_case_id()
    {
        var report = await Run();

        Assert.Equal(["A", "B", "C"], report.Cases.Select(c => c.CaseId));
    }
}
