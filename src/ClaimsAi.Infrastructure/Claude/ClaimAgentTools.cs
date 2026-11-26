using System.Text.Json;
using Anthropic.Models.Messages;
using ClaimsAi.Core;

namespace ClaimsAi.Infrastructure.Claude;

/// <summary>
/// The tools the triage agent may call: read-only lookups in the CRM and the policy documents.
/// Kept separate from the loop so they can be tested without calling the model.
/// </summary>
public sealed class ClaimAgentTools(IPolicyholderDirectory directory, IPolicyRetriever retriever)
{
    public const int PolicySearchResults = 5;

    internal static readonly Tool[] Definitions =
    [
        new()
        {
            Name = "get_policyholder",
            Description = "Look up a policy in the CRM: holder name, status (active/lapsed/cancelled), start and end dates, "
                + "address on file and insured vehicle plates. Returns found=false if the policy number doesn't exist.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["policy_number"] = JsonSerializer.SerializeToElement(new { type = "string", description = "e.g. AUTO-448812" }),
                },
                Required = ["policy_number"],
            },
        },
        new()
        {
            Name = "get_claim_history",
            Description = "List every claim previously filed by the holder of this policy, including under earlier policies: "
                + "date, type, amount and outcome. Use it to spot repeated or recent claims.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["policy_number"] = JsonSerializer.SerializeToElement(new { type = "string" }),
                },
                Required = ["policy_number"],
            },
        },
        new()
        {
            Name = "search_policy",
            Description = "Keyword search over the policy documents. Returns the most relevant clauses with their ids. "
                + "Use it for clauses the coverage decision didn't include, such as reporting deadlines, required documents or limits.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["query"] = JsonSerializer.SerializeToElement(new { type = "string", description = "e.g. 'burglary police report deadline'" }),
                    ["product_line"] = JsonSerializer.SerializeToElement(new { type = "string", @enum = new[] { "auto", "home" } }),
                },
                Required = ["query"],
            },
        },
    ];

    /// <summary>Runs one tool call. IsError is true for bad input or an unknown tool, so the model can correct itself.</summary>
    public (string Content, bool IsError) Execute(string name, IReadOnlyDictionary<string, JsonElement> input)
    {
        try
        {
            return name switch
            {
                "get_policyholder" => (GetPolicyholder(RequiredString(input, "policy_number")), false),
                "get_claim_history" => (GetClaimHistory(RequiredString(input, "policy_number")), false),
                "search_policy" => (SearchPolicy(RequiredString(input, "query"), OptionalString(input, "product_line")), false),
                _ => (Error($"Unknown tool '{name}'."), true),
            };
        }
        catch (ArgumentException ex)
        {
            return (Error(ex.Message), true);
        }
    }

    private string GetPolicyholder(string policyNumber)
    {
        var p = directory.Find(policyNumber);
        return p is null
            ? ClaimsJson.Serialize(new { Found = false, PolicyNumber = policyNumber })
            : ClaimsJson.Serialize(new { Found = true, p.PolicyNumber, p.HolderName, p.Status, p.StartDate, p.EndDate, p.Address, p.VehiclePlates });
    }

    private string GetClaimHistory(string policyNumber)
    {
        var p = directory.Find(policyNumber);
        return p is null
            ? ClaimsJson.Serialize(new { Found = false, PolicyNumber = policyNumber })
            : ClaimsJson.Serialize(new { Found = true, p.PolicyNumber, p.Claims });
    }

    private string SearchPolicy(string query, string? productLine)
    {
        var line = productLine is null ? null : ProductLines.FromPrefix(productLine);
        var clauses = retriever.Search(query, line, PolicySearchResults);
        return ClaimsJson.Serialize(clauses.Select(c => new { c.Id, c.Section, c.Text }));
    }

    private static string RequiredString(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        OptionalString(input, key) ?? throw new ArgumentException($"Missing required string '{key}'.");

    private static string? OptionalString(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        input.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } s
            ? s
            : null;

    private static string Error(string message) => ClaimsJson.Serialize(new { Error = message });
}
