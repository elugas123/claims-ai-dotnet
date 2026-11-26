using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaimsAi.Core;

/// <summary>
/// The JSON contract of <see cref="ClaimExtraction"/>: snake_case wire format and its JSON Schema.
/// Used by the API, the eval data files and as the structured-output schema for the model.
/// </summary>
public static class ClaimExtractionJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        WriteIndented = true,
    };

    public static ClaimExtraction Deserialize(string json) =>
        JsonSerializer.Deserialize<ClaimExtraction>(json, Options)
        ?? throw new JsonException("Model returned an empty extraction.");

    public static string Serialize(ClaimExtraction extraction) =>
        JsonSerializer.Serialize(extraction, Options);

    // Structured outputs require additionalProperties:false and every property listed in "required";
    // optional values are expressed as nullable instead.
    public const string SchemaJson = """
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
    """;

    public static Dictionary<string, JsonElement> Schema { get; } =
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(SchemaJson)!;
}
