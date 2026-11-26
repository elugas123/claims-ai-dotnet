namespace ClaimsAi.Core;

/// <summary>What should happen next with the claim.</summary>
public enum TriageAction
{
    /// <summary>Covered, complete, no warning signs: pay without a human review.</summary>
    FastTrack,

    /// <summary>A required fact or document is missing.</summary>
    RequestInformation,

    /// <summary>Covered but needs an adjuster (injuries, liability, large amounts, unclear cases).</summary>
    ManualReview,

    /// <summary>The evidence shows fraud signals worth investigating.</summary>
    ReferToFraudTeam,

    /// <summary>Not covered, or the policy wasn't active on the incident date.</summary>
    Deny,
}

/// <summary>A suspicious pattern and the evidence from the tools that supports it.</summary>
public sealed record FraudSignal(string Signal, string Evidence);

/// <summary>The agent's conclusion: next action, warning signs, what's missing and the reply to the customer.</summary>
public sealed record ClaimTriage
{
    public required TriageAction RecommendedAction { get; init; }
    public IReadOnlyList<FraudSignal> FraudSignals { get; init; } = [];
    public IReadOnlyList<string> MissingInformation { get; init; } = [];

    /// <summary>Written in the customer's language. Never mentions fraud checks.</summary>
    public string CustomerReply { get; init; } = "";

    /// <summary>Short notes for the adjuster, in English.</summary>
    public string InternalNotes { get; init; } = "";
}

/// <summary>One tool the agent decided to call, kept for auditing what it looked at.</summary>
public sealed record AgentToolCall(string Name, string Input, bool IsError);

public sealed record TriageResult(
    ClaimTriage Triage,
    IReadOnlyList<AgentToolCall> ToolCalls,
    int Turns,
    long InputTokens,
    long OutputTokens,
    TimeSpan Latency);

/// <summary>Investigates a claim with tools and recommends what to do. Implemented in Infrastructure.</summary>
public interface IClaimTriageAgent
{
    Task<TriageResult> TriageAsync(
        ClaimSubmission submission,
        ClaimExtraction claim,
        CoverageDecision coverage,
        CancellationToken cancellationToken = default);
}
