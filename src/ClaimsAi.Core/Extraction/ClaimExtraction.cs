namespace ClaimsAi.Core.Extraction;

public enum ClaimType
{
    AutoCollision,
    AutoTheft,
    AutoGlass,
    HomeWaterDamage,
    HomeFire,
    HomeBurglary,
    HomeStorm,
    Other,
}

/// <summary>Structured data extracted from a free-text claim report.</summary>
public sealed record ClaimExtraction
{
    public required ClaimType ClaimType { get; init; }

    /// <summary>ISO date (yyyy-MM-dd), or null when the report doesn't pin down a single day.</summary>
    public string? IncidentDate { get; init; }

    public string? Location { get; init; }
    public string? PolicyNumber { get; init; }
    public string? VehiclePlate { get; init; }
    public decimal? EstimatedAmount { get; init; }

    /// <summary>ISO 4217 code (USD, ARS, ...).</summary>
    public string? Currency { get; init; }

    public required bool InjuriesReported { get; init; }
    public required bool PoliceReportFiled { get; init; }
    public required bool ThirdPartyInvolved { get; init; }

    /// <summary>One-sentence summary in English, whatever the input language.</summary>
    public string Summary { get; init; } = "";

    /// <summary>Information the policy requires that the report doesn't include.</summary>
    public IReadOnlyList<string> MissingInformation { get; init; } = [];
}

/// <summary>A claim as it arrives: the customer's text plus the day it was received (to resolve "yesterday").</summary>
public sealed record ClaimSubmission(string Text, DateOnly ReceivedAt);

public sealed record ExtractionResult(
    ClaimExtraction Extraction,
    long InputTokens,
    long OutputTokens,
    TimeSpan Latency);

public interface IClaimExtractor
{
    Task<ExtractionResult> ExtractAsync(ClaimSubmission submission, CancellationToken cancellationToken = default);
}
