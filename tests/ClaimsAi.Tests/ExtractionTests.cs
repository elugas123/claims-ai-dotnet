using System.Text.Json;
using ClaimsAi.Core.Evals;
using ClaimsAi.Core.Extraction;

namespace ClaimsAi.Tests;

public class SchemaTests
{
    [Fact]
    public void Schema_lists_every_property_as_required_and_forbids_extras()
    {
        var schema = ClaimExtractionJson.Schema;
        var properties = schema["properties"].EnumerateObject().Select(p => p.Name).ToHashSet();
        var required = schema["required"].EnumerateArray().Select(e => e.GetString()!).ToHashSet();

        Assert.False(schema["additionalProperties"].GetBoolean());
        Assert.Equal(properties, required);
    }

    [Fact]
    public void Schema_properties_match_the_csharp_record()
    {
        var properties = ClaimExtractionJson.Schema["properties"].EnumerateObject().Select(p => p.Name).ToHashSet();
        var recordProperties = typeof(ClaimExtraction).GetProperties()
            .Select(p => JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name))
            .ToHashSet();

        Assert.Equal(recordProperties, properties);
    }

    [Fact]
    public void Schema_claim_types_match_the_enum()
    {
        var schemaValues = ClaimExtractionJson.Schema["properties"].GetProperty("claim_type").GetProperty("enum")
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

        var extraction = ClaimExtractionJson.Deserialize(json);

        Assert.Equal(ClaimType.HomeWaterDamage, extraction.ClaimType);
        Assert.Equal(3500m, extraction.EstimatedAmount);
        Assert.Null(extraction.VehiclePlate);
        Assert.Equal(["plumber's report"], extraction.MissingInformation);
    }
}

public class EvalSetTests
{
    private static readonly string EvalSetPath = Path.Combine(AppContext.BaseDirectory, "data", "claims", "eval-set.json");

    [Fact]
    public void Eval_set_loads_with_unique_ids()
    {
        var cases = EvalCase.LoadFile(EvalSetPath);

        Assert.True(cases.Count >= 12);
        Assert.Equal(cases.Count, cases.Select(c => c.Id).Distinct().Count());
        Assert.All(cases, c => Assert.False(string.IsNullOrWhiteSpace(c.Text)));
    }

    [Fact]
    public void Eval_set_covers_every_claim_type()
    {
        var types = EvalCase.LoadFile(EvalSetPath).Select(c => c.Expected.ClaimType).ToHashSet();

        Assert.Equal(Enum.GetValues<ClaimType>().ToHashSet(), types);
    }

    [Fact]
    public void Labels_score_perfectly_against_themselves()
    {
        foreach (var c in EvalCase.LoadFile(EvalSetPath))
        {
            Assert.True(ExtractionScorer.Score(c.Id, c.Expected, c.Expected).Perfect, c.Id);
        }
    }
}

public class ScorerTests
{
    private static readonly ClaimExtraction Label = new()
    {
        ClaimType = ClaimType.AutoCollision,
        IncidentDate = "2026-11-16",
        Location = "Av. Belgrano 1200, Rosario",
        PolicyNumber = "AUTO-883301",
        VehiclePlate = "AB123CD",
        EstimatedAmount = 1200000,
        Currency = "ARS",
        InjuriesReported = false,
        PoliceReportFiled = false,
        ThirdPartyInvolved = false,
    };

    private static FieldScore Field(CaseScore score, string name) => score.Fields.Single(f => f.Field == name);

    [Fact]
    public void Plates_and_policy_numbers_ignore_formatting()
    {
        var score = ExtractionScorer.Score("x", Label, Label with { VehiclePlate = "ab 123 cd", PolicyNumber = "auto 883301" });

        Assert.True(Field(score, "vehicle_plate").Match);
        Assert.True(Field(score, "policy_number").Match);
    }

    [Fact]
    public void Location_tolerates_rewording_but_not_a_different_place()
    {
        var reworded = ExtractionScorer.Score("x", Label, Label with { Location = "Belgrano Av. 1200, Rosario, Santa Fe" });
        var different = ExtractionScorer.Score("x", Label, Label with { Location = "Calle Mitre 455, Córdoba" });

        Assert.True(Field(reworded, "location").Match);
        Assert.False(Field(different, "location").Match);
    }

    [Fact]
    public void Wrong_date_and_invented_values_are_misses()
    {
        var score = ExtractionScorer.Score("x", Label with { VehiclePlate = null }, Label with { IncidentDate = "2026-11-17" });

        Assert.False(Field(score, "incident_date").Match);
        Assert.False(Field(score, "vehicle_plate").Match);
        Assert.Equal(8, score.Matches);
        Assert.False(score.Perfect);
    }
}
