using Anthropic;
using Anthropic.Models.Messages;
using ClaimsAi.Infrastructure.Claude;
using Xunit.Abstractions;

namespace ClaimsAi.Evals;

/// <summary>
/// Runs the real Claude extractor over the labeled eval set. Costs a few cents per run, so it only runs
/// when ANTHROPIC_API_KEY is set. Run just this test with:
///   dotnet test --filter Category=LiveEval --logger "console;verbosity=detailed"
/// </summary>
public class LiveExtractionEval(ITestOutputHelper output)
{
    private const string Model = "claude-opus-5-5";
    private static readonly Effort Effort = Effort.Low;

    // Gate on the overall score, not on single cases: model output varies a little between runs.
    private const double MinAccuracy = 0.85;

    [RequiresApiKeyFact]
    [Trait("Category", "LiveEval")]
    public async Task Extraction_accuracy_stays_above_threshold()
    {
        var cases = EvalCase.LoadFile(EvalPaths.EvalSet);
        var extractor = new ClaudeClaimExtractor(
            new AnthropicClient(),
            new ClaudeOptions { Model = Model, Effort = Effort });

        var report = await EvalRunner.RunAsync(
            cases, extractor, Model, Effort.ToString().ToLowerInvariant(), ModelPricing.ClaudeOpus55);

        output.WriteLine(report.ToText());
        output.WriteLine($"Saved {report.SaveTo(EvalPaths.ResultsDir)}");

        Assert.True(
            report.OverallAccuracy >= MinAccuracy,
            $"Accuracy {report.OverallAccuracy:P1} is below the {MinAccuracy:P0} threshold.");
    }
}

/// <summary>A [Fact] that is skipped when no Anthropic API key is configured.</summary>
public sealed class RequiresApiKeyFactAttribute : FactAttribute
{
    public RequiresApiKeyFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
        {
            Skip = "Set ANTHROPIC_API_KEY to run live evals.";
        }
    }
}

/// <summary>Repo paths, found by walking up from the test binaries to the folder with ClaimsAi.slnx.</summary>
public static class EvalPaths
{
    public static string RepoRoot { get; } = FindRepoRoot();
    public static string EvalSet => Path.Combine(RepoRoot, "data", "claims", "eval-set.json");
    public static string ResultsDir => Path.Combine(RepoRoot, "evals", "results");

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ClaimsAi.slnx")))
            {
                return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException("Could not find ClaimsAi.slnx above " + AppContext.BaseDirectory);
    }
}
