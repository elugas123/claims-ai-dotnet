namespace ClaimsAi.Core;

/// <summary>A claim as it arrives: the customer's text plus the day it was received (to resolve "yesterday").</summary>
public sealed record ClaimSubmission(string Text, DateOnly ReceivedAt);
