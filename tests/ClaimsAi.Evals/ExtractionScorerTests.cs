using ClaimsAi.Core;

namespace ClaimsAi.Evals;

public class EvalSetTests
{
    [Fact]
    public void Eval_set_loads_with_unique_ids()
    {
        var cases = EvalCase.LoadFile(EvalPaths.EvalSet);

        Assert.True(cases.Count >= 12);
        Assert.Equal(cases.Count, cases.Select(c => c.Id).Distinct().Count());
        Assert.All(cases, c => Assert.False(string.IsNullOrWhiteSpace(c.Text)));
    }

    [Fact]
    public void Eval_set_covers_every_claim_type()
    {
        var types = EvalCase.LoadFile(EvalPaths.EvalSet).Select(c => c.Expected.ClaimType).ToHashSet();

        Assert.Equal(Enum.GetValues<ClaimType>().ToHashSet(), types);
    }

    [Fact]
    public void Labels_score_perfectly_against_themselves()
    {
        foreach (var c in EvalCase.LoadFile(EvalPaths.EvalSet))
        {
            Assert.True(ExtractionScorer.Score(c.Id, c.Expected, c.Expected).Perfect, c.Id);
        }
    }
}

public class ExtractionScorerTests
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
