namespace ClaimsAi.Core;

public enum PolicyStatus
{
    Active,
    Lapsed,
    Cancelled,
}

public sealed record PastClaim(DateOnly Date, ClaimType ClaimType, decimal Amount, string Currency, string Outcome);

/// <summary>The insurer's record of a policy and its holder, including every claim the holder filed before.</summary>
public sealed record Policyholder(
    string PolicyNumber,
    string HolderName,
    PolicyStatus Status,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Address,
    IReadOnlyList<string> VehiclePlates,
    IReadOnlyList<PastClaim> Claims);

/// <summary>Looks up policyholders (the insurer's CRM). Implemented in Infrastructure.</summary>
public interface IPolicyholderDirectory
{
    Policyholder? Find(string policyNumber);
}
