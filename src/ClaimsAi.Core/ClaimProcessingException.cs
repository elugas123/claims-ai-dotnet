namespace ClaimsAi.Core;

/// <summary>An AI step could not produce a valid result (declined, truncated or malformed output).</summary>
public sealed class ClaimProcessingException(string message) : Exception(message);
