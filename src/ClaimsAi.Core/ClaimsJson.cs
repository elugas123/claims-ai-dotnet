using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaimsAi.Core;

/// <summary>
/// The JSON contract of the domain: snake_case wire format plus the JSON Schemas the model must follow
/// (structured outputs). Used by the API, the eval data files and the Claude implementations.
/// </summary>
public static class ClaimsJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        WriteIndented = true,
    };

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new JsonException($"Empty JSON for {typeof(T).Name}.");

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    // Structured outputs require additionalProperties:false and every property listed in "required";
    // optional values are expressed as nullable instead.

    /// <summary>Schema of <see cref="ClaimExtraction"/>.</summary>
    public static Dictionary<string, JsonElement> ExtractionSchema { get; } = Parse("""
    {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "claim_type": {
          "type": "string",
          "enum": ["auto_collision", "auto_theft", "auto_glass", "home_water_damage", "home_fire", "home_burglary", "home_storm", "other"]
        },
        "incident_date": { "anyOf": [{ "type": "string", "format": "date" }, { "type": "null" }] },
        "location": { "anyOf": [{ "type": "string" }, { "type": "null" }] },
        "policy_number": { "anyOf": [{ "type": "string" }, { "type": "null" }] },
        "vehicle_plate": { "anyOf": [{ "type": "string" }, { "type": "null" }] },
        "estimated_amount": { "anyOf": [{ "type": "number" }, { "type": "null" }] },
        "currency": { "anyOf": [{ "type": "string" }, { "type": "null" }] },
        "injuries_reported": { "type": "boolean" },
        "police_report_filed": { "type": "boolean" },
        "third_party_involved": { "type": "boolean" },
        "summary": { "type": "string" },
        "missing_information": { "type": "array", "items": { "type": "string" } }
      },
      "required": [
        "claim_type", "incident_date", "location", "policy_number", "vehicle_plate", "estimated_amount",
        "currency", "injuries_reported", "police_report_filed", "third_party_involved", "summary", "missing_information"
      ]
    }
    """);

    /// <summary>Schema of <see cref="CoverageDecision"/>.</summary>
    public static Dictionary<string, JsonElement> CoverageSchema { get; } = Parse("""
    {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "verdict": { "type": "string", "enum": ["covered", "not_covered", "needs_review"] },
        "cited_clauses": { "type": "array", "items": { "type": "string" } },
        "deductible": { "anyOf": [{ "type": "string" }, { "type": "null" }] },
        "required_documents": { "type": "array", "items": { "type": "string" } },
        "reasoning": { "type": "string" }
      },
      "required": ["verdict", "cited_clauses", "deductible", "required_documents", "reasoning"]
    }
    """);

    private static Dictionary<string, JsonElement> Parse(string schema) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(schema)!;
}
