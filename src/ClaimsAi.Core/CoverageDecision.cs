namespace ClaimsAi.Core;

public enum CoverageVerdict
{
    Covered,
    NotCovered,

    /// <summary>Coverage depends on missing information or a condition that may not be met.</summary>
    NeedsReview,
}

/// <summary>Whether a claim is covered, backed by citations to the policy clauses that drive the decision.</summary>
public sealed record CoverageDecision
{
    public required CoverageVerdict Verdict { get; init; }

    /// <summary>Clause ids such as "AUTO-COMP-2026#1.1". Only clauses that were given to the model.</summary>
    public IReadOnlyList<string> CitedClauses { get; init; } = [];

    public string? Deductible { get; init; }
    public IReadOnlyList<string> RequiredDocuments { get; init; } = [];
    public string Reasoning { get; init; } = "";
}

public sealed record CoverageResult(CoverageDecision Decision, long InputTokens, long OutputTokens, TimeSpan Latency);

/// <summary>Decides coverage for a claim from a set of retrieved policy clauses. Implemented in Infrastructure.</summary>
public interface ICoverageChecker
{
    Task<CoverageResult> CheckAsync(
        ClaimSubmission submission,
        ClaimExtraction claim,
        IReadOnlyList<PolicyClause> clauses,
        CancellationToken cancellationToken = default);
}
