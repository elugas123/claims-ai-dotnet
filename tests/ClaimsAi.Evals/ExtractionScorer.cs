using System.Text.Json;
using ClaimsAi.Core;

namespace ClaimsAi.Evals;

/// <summary>A labeled claim: input text plus the extraction and coverage decision a careful human would produce.</summary>
public sealed record EvalCase(
    string Id,
    DateOnly ReceivedAt,
    string Text,
    ClaimExtraction Expected,
    ExpectedCoverage ExpectedCoverage,
    ExpectedTriage ExpectedTriage)
{
    public ClaimSubmission ToSubmission() => new(Text, ReceivedAt);

    public static IReadOnlyList<EvalCase> LoadFile(string path) =>
        JsonSerializer.Deserialize<List<EvalCase>>(File.ReadAllText(path), ClaimsJson.Options)
        ?? throw new JsonException($"No eval cases in {path}");
}

/// <summary>The right verdict and the clauses a correct answer must rely on.</summary>
public sealed record ExpectedCoverage(CoverageVerdict Verdict, IReadOnlyList<string> Clauses);

/// <summary>The right next action and whether the records hold fraud signals.</summary>
public sealed record ExpectedTriage(TriageAction Action, bool Fraud);

public sealed record FieldScore(string Field, bool Match, string? Expected, string? Actual);

public sealed record CaseScore(string CaseId, IReadOnlyList<FieldScore> Fields)
{
    public int Matches => Fields.Count(f => f.Match);
    public double Accuracy => Fields.Count == 0 ? 0 : (double)Matches / Fields.Count;
    public bool Perfect => Matches == Fields.Count;
}

/// <summary>
/// Compares a predicted extraction against the label, field by field.
/// Free-text fields (summary, missing_information) are not scored here.
/// </summary>
public static class ExtractionScorer
{
    public static CaseScore Score(string caseId, ClaimExtraction expected, ClaimExtraction actual) => new(caseId,
    [
        Exact("claim_type", expected.ClaimType.ToString(), actual.ClaimType.ToString()),
        Exact("incident_date", expected.IncidentDate, actual.IncidentDate),
        Location(expected.Location, actual.Location),
        Exact("policy_number", Normalize(expected.PolicyNumber), Normalize(actual.PolicyNumber)),
        Exact("vehicle_plate", Normalize(expected.VehiclePlate), Normalize(actual.VehiclePlate)),
        Exact("estimated_amount", expected.EstimatedAmount?.ToString("0.##"), actual.EstimatedAmount?.ToString("0.##")),
        Exact("currency", expected.Currency?.ToUpperInvariant(), actual.Currency?.ToUpperInvariant()),
        Exact("injuries_reported", expected.InjuriesReported.ToString(), actual.InjuriesReported.ToString()),
        Exact("police_report_filed", expected.PoliceReportFiled.ToString(), actual.PoliceReportFiled.ToString()),
        Exact("third_party_involved", expected.ThirdPartyInvolved.ToString(), actual.ThirdPartyInvolved.ToString()),
    ]);

    private static FieldScore Exact(string field, string? expected, string? actual) =>
        new(field, string.Equals(expected, actual, StringComparison.Ordinal), expected, actual);

    /// <summary>Plates and policy numbers are compared ignoring case, spaces and dashes ("ab 123-cd" == "AB123CD").</summary>
    private static string? Normalize(string? value) =>
        value is null ? null : new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>
    /// Addresses can be written many ways, so a location matches when most of the label's words appear in the prediction.
    /// </summary>
    private static FieldScore Location(string? expected, string? actual)
    {
        if (expected is null || actual is null)
        {
            return new("location", expected is null && actual is null, expected, actual);
        }

        var expectedTokens = Tokens(expected);
        var actualTokens = Tokens(actual);
        var overlap = expectedTokens.Count(actualTokens.Contains);
        var match = expectedTokens.Count > 0 && (double)overlap / expectedTokens.Count >= 0.6;
        return new("location", match, expected, actual);
    }

    private static HashSet<string> Tokens(string text) =>
        text.ToLowerInvariant()
            .Split([' ', ',', '.', '-', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();
}
