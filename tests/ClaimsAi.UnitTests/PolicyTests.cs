using System.Text.Json;
using ClaimsAi.Core;
using ClaimsAi.Infrastructure.Policies;

namespace ClaimsAi.UnitTests;

public class MarkdownPolicyLoaderTests
{
    private const string Policy = """
        # Contoso Insurance — Auto Policy (AUTO-TEST-2026)

        > Synthetic document.

        ## 1. Covered events

        1.1 **Collision.** Damage caused by a collision.

        1.2 **Theft.** Total theft of the vehicle.
        A police report must be filed within 48 hours.

        ## 2. Exclusions

        2.1 Mechanical failure or wear and tear.
        """;

    [Fact]
    public void Splits_numbered_clauses_with_their_section()
    {
        var clauses = MarkdownPolicyLoader.Parse(Policy);

        Assert.Equal(["AUTO-TEST-2026#1.1", "AUTO-TEST-2026#1.2", "AUTO-TEST-2026#2.1"], clauses.Select(c => c.Id));
        Assert.Equal("Covered events", clauses[0].Section);
        Assert.Equal("Exclusions", clauses[2].Section);
        Assert.Equal(ProductLine.Auto, clauses[0].ProductLine);
    }

    [Fact]
    public void Joins_continuation_lines_and_strips_markdown()
    {
        var theft = MarkdownPolicyLoader.Parse(Policy)[1];

        Assert.Equal("Theft. Total theft of the vehicle. A police report must be filed within 48 hours.", theft.Text);
    }

    [Fact]
    public void Title_without_policy_id_is_rejected()
    {
        Assert.Throws<FormatException>(() => MarkdownPolicyLoader.Parse("# Some policy\n\n1.1 Text"));
    }
}

public class Bm25PolicyRetrieverTests
{
    private static readonly IReadOnlyList<PolicyClause> Clauses =
    [
        new("AUTO-X", "1.1", "Covered events", "Collision. Damage to the vehicle caused by a collision with another vehicle."),
        new("AUTO-X", "1.3", "Covered events", "Glass. Repair or replacement of windshield and windows."),
        new("AUTO-X", "2.5", "Exclusions", "Mechanical failure, wear and tear, or lack of maintenance."),
        new("HOME-X", "1.2", "Covered events", "Water damage. Sudden escape of water from pipes."),
    ];

    private readonly Bm25PolicyRetriever _retriever = new(Clauses);

    [Fact]
    public void Ranks_the_matching_clause_first()
    {
        var results = _retriever.Search("a rock cracked my windshield", productLine: null, top: 3);

        Assert.Equal("AUTO-X#1.3", results[0].Id);
    }

    [Fact]
    public void Filters_by_product_line()
    {
        var results = _retriever.Search("damage", ProductLine.Home, top: 5);

        Assert.All(results, r => Assert.Equal(ProductLine.Home, r.ProductLine));
    }

    [Fact]
    public void Returns_nothing_when_no_word_matches()
    {
        Assert.Empty(_retriever.Search("zebra", productLine: null, top: 5));
    }

    [Fact]
    public void Tokenizer_drops_stop_words_and_plurals()
    {
        Assert.Equal(["claim", "window", "glass"], Bm25PolicyRetriever.Tokenize("The claims for my windows, glass!"));
    }
}

public class ClaimAssessmentTests
{
    private static ClaimExtraction Claim(ClaimType type, string? policy = null) => new()
    {
        ClaimType = type,
        PolicyNumber = policy,
        InjuriesReported = false,
        PoliceReportFiled = false,
        ThirdPartyInvolved = false,
    };

    [Theory]
    [InlineData(ClaimType.HomeFire, "AUTO-1", ProductLine.Auto)]   // the policy number wins
    [InlineData(ClaimType.HomeFire, null, ProductLine.Home)]       // otherwise the claim type
    [InlineData(ClaimType.AutoGlass, null, ProductLine.Auto)]
    [InlineData(ClaimType.Other, null, null)]                      // unknown: search every policy
    public void Product_line_comes_from_policy_number_then_claim_type(ClaimType type, string? policy, ProductLine? expected)
    {
        Assert.Equal(expected, ProductLines.For(Claim(type, policy)));
    }

    [Fact]
    public void Query_spells_out_the_claim_type_and_includes_the_report()
    {
        var query = ClaimAssessmentService.BuildQuery(new ClaimSubmission("pipe burst", DateOnly.MinValue), Claim(ClaimType.HomeWaterDamage));

        Assert.Contains("Home Water Damage", query);
        Assert.Contains("pipe burst", query);
    }

    [Fact]
    public void Claims_of_type_other_also_search_the_exclusions()
    {
        var query = ClaimAssessmentService.BuildQuery(new ClaimSubmission("engine died", DateOnly.MinValue), Claim(ClaimType.Other));

        Assert.Contains("exclusions", query);
    }
}

public class CoverageSchemaTests
{
    [Fact]
    public void Coverage_schema_matches_the_record_and_enum()
    {
        var schema = ClaimsJson.CoverageSchema;
        var properties = schema["properties"].EnumerateObject().Select(p => p.Name).ToHashSet();
        var required = schema["required"].EnumerateArray().Select(e => e.GetString()!).ToHashSet();
        var recordProperties = typeof(CoverageDecision).GetProperties()
            .Select(p => JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name)).ToHashSet();
        var verdicts = schema["properties"].GetProperty("verdict").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()!).ToHashSet();

        Assert.False(schema["additionalProperties"].GetBoolean());
        Assert.Equal(properties, required);
        Assert.Equal(recordProperties, properties);
        Assert.Equal(Enum.GetNames<CoverageVerdict>().Select(JsonNamingPolicy.SnakeCaseLower.ConvertName).ToHashSet(), verdicts);
    }
}
