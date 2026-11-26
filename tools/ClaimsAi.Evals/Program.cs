using System.Globalization;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using ClaimsAi.Core.Evals;
using ClaimsAi.Core.Extraction;

// Runs the extractor over the labeled eval set and reports accuracy per field, cost and latency.
//
//   dotnet run --project tools/ClaimsAi.Evals -- [--model claude-opus-5-5] [--effort low] [--concurrency 4]
//                                               [--min-accuracy 0.9] [--data path] [--out dir] [--self-check]
//
// --self-check feeds the labels back as predictions: no API calls, it only verifies the pipeline end to end.

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var options = EvalOptions.Parse(args);
if (options is null)
{
    return 2;
}

var cases = EvalCase.LoadFile(options.DataPath);

IClaimExtractor extractor;
if (options.SelfCheck)
{
    extractor = new LabelEchoExtractor(cases);
}
else
{
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
    {
        Console.Error.WriteLine("""
            ANTHROPIC_API_KEY is not set.
            Set it for your user (PowerShell), then open a new terminal:
              [Environment]::SetEnvironmentVariable('ANTHROPIC_API_KEY', '<your-key>', 'User')
            Or run with --self-check to test the pipeline without calling the API.
            """);
        return 2;
    }

    extractor = new ClaudeClaimExtractor(
        new AnthropicClient(),
        new ClaudeExtractorOptions { Model = options.Model, Effort = options.Effort });
}

var effortLabel = options.SelfCheck ? "self-check" : options.Effort.ToString().ToLowerInvariant();
Console.WriteLine($"claims-ai-dotnet eval · {options.Model} · effort {effortLabel} · {cases.Count} cases");
Console.WriteLine();

var progress = new Progress<CaseResult>(PrintCase);
var report = await EvalRunner.RunAsync(
    cases, extractor, options.Model, effortLabel, ModelPricing.ClaudeOpus55, options.Concurrency, progress);

// Progress<T> posts callbacks asynchronously; give the last ones a moment to print before the summary.
await Task.Delay(100);
PrintSummary(report);

var savedPath = Save(report, options.OutDir);
Console.WriteLine($"Saved {Path.GetRelativePath(Environment.CurrentDirectory, savedPath)}");

if (report.OverallAccuracy < options.MinAccuracy)
{
    Console.WriteLine($"FAIL: accuracy {report.OverallAccuracy:P1} is below the {options.MinAccuracy:P0} threshold.");
    return 1;
}
return 0;

static void PrintCase(CaseResult result)
{
    if (result.Error is not null)
    {
        Console.WriteLine($"  {result.CaseId}  ERROR  {result.Error}");
        return;
    }

    var status = result.Score!.Perfect ? "PASS" : "FAIL";
    var seconds = result.Extraction!.Latency.TotalSeconds;
    Console.WriteLine($"  {result.CaseId}  {status}  {result.Score.Matches,2}/{result.Score.Fields.Count}  {seconds,5:0.0}s");
    foreach (var miss in result.Score.Fields.Where(f => !f.Match))
    {
        Console.WriteLine($"          {miss.Field}: expected {miss.Expected ?? "null"}, got {miss.Actual ?? "null"}");
    }
}

static void PrintSummary(EvalReport report)
{
    Console.WriteLine();
    Console.WriteLine("Field accuracy");
    foreach (var f in report.FieldAccuracies)
    {
        Console.WriteLine($"  {f.Field,-22} {f.Accuracy,7:P1}  {f.Matches}/{f.Total}");
    }

    Console.WriteLine();
    Console.WriteLine($"Overall   {report.OverallAccuracy:P1} of fields · {report.PerfectCases}/{report.Cases.Count} cases perfect · {report.Errors} errors");
    Console.WriteLine($"Tokens    {report.InputTokens:N0} in · {report.OutputTokens:N0} out · ${report.CostUsd:0.0000}");
    Console.WriteLine($"Latency   p50 {report.LatencyP50.TotalSeconds:0.0}s · p95 {report.LatencyP95.TotalSeconds:0.0}s");
}

static string Save(EvalReport report, string outDir)
{
    Directory.CreateDirectory(outDir);
    var model = report.Model.Replace(':', '-').Replace('/', '-');
    var path = Path.Combine(outDir, $"{report.RanAt:yyyyMMdd-HHmmss}-{model}-{report.Effort}.json");
    File.WriteAllText(path, JsonSerializer.Serialize(report, ClaimExtractionJson.Options));
    return path;
}

internal sealed record EvalOptions(
    string Model,
    Effort Effort,
    int Concurrency,
    double MinAccuracy,
    string DataPath,
    string OutDir,
    bool SelfCheck)
{
    public static EvalOptions? Parse(string[] args)
    {
        var root = FindRepoRoot();
        var model = "claude-opus-5-5";
        var effort = Effort.Low;
        var concurrency = 4;
        var minAccuracy = 0.0;
        var dataPath = Path.Combine(root, "data", "claims", "eval-set.json");
        var outDir = Path.Combine(root, "evals", "results");
        var selfCheck = false;

        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");

            switch (args[i])
            {
                case "--model": model = Next(); break;
                case "--effort":
                    var value = Next();
                    if (!Enum.TryParse(value, ignoreCase: true, out effort))
                    {
                        Console.Error.WriteLine($"Unknown effort '{value}'. Use low, medium, high or max.");
                        return null;
                    }
                    break;
                case "--concurrency": concurrency = int.Parse(Next()); break;
                case "--min-accuracy": minAccuracy = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--data": dataPath = Path.GetFullPath(Next()); break;
                case "--out": outDir = Path.GetFullPath(Next()); break;
                case "--self-check": selfCheck = true; break;
                default:
                    Console.Error.WriteLine($"Unknown argument '{args[i]}'.");
                    return null;
            }
        }

        if (!File.Exists(dataPath))
        {
            Console.Error.WriteLine($"Eval set not found: {dataPath}");
            return null;
        }

        return new EvalOptions(model, effort, concurrency, minAccuracy, dataPath, outDir, selfCheck);
    }

    /// <summary>Walks up from the current directory to the folder that holds ClaimsAi.sln.</summary>
    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ClaimsAi.sln")))
            {
                return dir.FullName;
            }
        }
        return Environment.CurrentDirectory;
    }
}

/// <summary>Returns each case's label as the prediction. Used by --self-check to exercise the pipeline offline.</summary>
internal sealed class LabelEchoExtractor(IReadOnlyList<EvalCase> cases) : IClaimExtractor
{
    private readonly Dictionary<string, ClaimExtraction> _labels = cases.ToDictionary(c => c.Text, c => c.Expected);

    public Task<ExtractionResult> ExtractAsync(ClaimSubmission submission, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ExtractionResult(_labels[submission.Text], 0, 0, TimeSpan.Zero));
}
