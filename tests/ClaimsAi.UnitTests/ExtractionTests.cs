using System.Text.Json;
using ClaimsAi.Core;

namespace ClaimsAi.UnitTests;

public class SchemaTests
{
    [Fact]
    public void Schema_lists_every_property_as_required_and_forbids_extras()
    {
        var schema = ClaimsJson.ExtractionSchema;
        var properties = schema["properties"].EnumerateObject().Select(p => p.Name).ToHashSet();
        var required = schema["required"].EnumerateArray().Select(e => e.GetString()!).ToHashSet();

        Assert.False(schema["additionalProperties"].GetBoolean());
        Assert.Equal(properties, required);
    }

    [Fact]
    public void Schema_properties_match_the_csharp_record()
    {
        var properties = ClaimsJson.ExtractionSchema["properties"].EnumerateObject().Select(p => p.Name).ToHashSet();
        var recordProperties = typeof(ClaimExtraction).GetProperties()
            .Select(p => JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name))
            .ToHashSet();

        Assert.Equal(recordProperties, properties);
    }

    [Fact]
    public void Schema_claim_types_match_the_enum()
    {
        var schemaValues = ClaimsJson.ExtractionSchema["properties"].GetProperty("claim_type").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()!).ToHashSet();
        var enumValues = Enum.GetNames<ClaimType>().Select(JsonNamingPolicy.SnakeCaseLower.ConvertName).ToHashSet();

        Assert.Equal(enumValues, schemaValues);
    }
}

public class JsonTests
{
    [Fact]
    public void Deserializes_a_model_response()
    {
        const string json = """
        {
          "claim_type": "home_water_damage",
          "incident_date": "2026-11-08",
          "location": "14 Oak Lane, Riverton",
          "policy_number": "HOME-330145",
          "vehicle_plate": null,
          "estimated_amount": 3500,
          "currency": "USD",
          "injuries_reported": false,
          "police_report_filed": false,
          "third_party_involved": false,
          "summary": "A burst pipe flooded the kitchen.",
          "missing_information": ["plumber's report"]
        }
        """;

        var extraction = ClaimsJson.Deserialize<ClaimExtraction>(json);

        Assert.Equal(ClaimType.HomeWaterDamage, extraction.ClaimType);
        Assert.Equal(3500m, extraction.EstimatedAmount);
        Assert.Null(extraction.VehiclePlate);
        Assert.Equal(["plumber's report"], extraction.MissingInformation);
    }
}
