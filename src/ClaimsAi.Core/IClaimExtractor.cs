namespace ClaimsAi.Core;

/// <summary>Turns a free-text claim into structured data. Implemented in Infrastructure (e.g. with Claude).</summary>
public interface IClaimExtractor
{
    Task<ExtractionResult> ExtractAsync(ClaimSubmission submission, CancellationToken cancellationToken = default);
}

/// <summary>The extraction plus what it cost, so callers can track tokens and latency.</summary>
public sealed record ExtractionResult(
    ClaimExtraction Extraction,
    long InputTokens,
    long OutputTokens,
    TimeSpan Latency);
