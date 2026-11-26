using ClaimsAi.Core;

namespace ClaimsAi.Infrastructure.Crm;

/// <summary>A fake CRM backed by a JSON file. Stands in for the insurer's real policy system.</summary>
public sealed class JsonPolicyholderDirectory : IPolicyholderDirectory
{
    private readonly Dictionary<string, Policyholder> _byPolicy;

    public JsonPolicyholderDirectory(IEnumerable<Policyholder> policyholders) =>
        _byPolicy = policyholders.ToDictionary(p => Normalize(p.PolicyNumber));

    public static JsonPolicyholderDirectory LoadFile(string path) =>
        new(ClaimsJson.Deserialize<List<Policyholder>>(File.ReadAllText(path)));

    public Policyholder? Find(string policyNumber) => _byPolicy.GetValueOrDefault(Normalize(policyNumber));

    /// <summary>"auto 448812" and "AUTO-448812" are the same policy.</summary>
    private static string Normalize(string policyNumber) =>
        new(policyNumber.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
