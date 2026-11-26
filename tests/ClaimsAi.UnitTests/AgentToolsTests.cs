using System.Text.Json;
using ClaimsAi.Core;
using ClaimsAi.Infrastructure.Claude;
using ClaimsAi.Infrastructure.Crm;
using ClaimsAi.Infrastructure.Policies;

namespace ClaimsAi.UnitTests;

public class AgentToolsTests
{
    private static readonly Policyholder Holder = new(
        "HOME-771204", "Marcus Reed", PolicyStatus.Active, new DateOnly(2024, 1, 15), new DateOnly(2027, 1, 15),
        "220 Pine Ave, Apt 5B, Lakeside", [],
        [new PastClaim(new DateOnly(2026, 2, 3), ClaimType.HomeBurglary, 3500, "USD", "paid")]);

    private readonly ClaimAgentTools _tools = new(
        new JsonPolicyholderDirectory([Holder]),
        new Bm25PolicyRetriever([new PolicyClause("HOME-STD-2026", "4.3", "Reporting obligations", "Burglary claims require a police report number.")]));

    private static Dictionary<string, JsonElement> Input(object value) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(value))!;

    private static JsonElement Json(string content) => JsonDocument.Parse(content).RootElement;

    [Fact]
    public void Policyholder_lookup_ignores_formatting_of_the_policy_number()
    {
        var (content, isError) = _tools.Execute("get_policyholder", Input(new { policy_number = "home 771204" }));

        Assert.False(isError);
        Assert.True(Json(content).GetProperty("found").GetBoolean());
        Assert.Equal("active", Json(content).GetProperty("status").GetString());
    }

    [Fact]
    public void Unknown_policy_is_a_normal_answer_not_an_error()
    {
        var (content, isError) = _tools.Execute("get_policyholder", Input(new { policy_number = "AUTO-000000" }));

        Assert.False(isError);
        Assert.False(Json(content).GetProperty("found").GetBoolean());
    }

    [Fact]
    public void Claim_history_lists_past_claims()
    {
        var (content, _) = _tools.Execute("get_claim_history", Input(new { policy_number = "HOME-771204" }));

        var claims = Json(content).GetProperty("claims");
        Assert.Equal(1, claims.GetArrayLength());
        Assert.Equal("home_burglary", claims[0].GetProperty("claim_type").GetString());
    }

    [Fact]
    public void Policy_search_returns_clause_ids()
    {
        var (content, _) = _tools.Execute("search_policy", Input(new { query = "burglary police report", product_line = "home" }));

        Assert.Equal("HOME-STD-2026#4.3", Json(content)[0].GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("get_policyholder", "{}")]
    [InlineData("delete_policy", """{"policy_number":"HOME-771204"}""")]
    public void Bad_input_or_unknown_tool_returns_an_error_the_model_can_read(string tool, string input)
    {
        var (content, isError) = _tools.Execute(tool, JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(input)!);

        Assert.True(isError);
        Assert.True(Json(content).TryGetProperty("error", out _));
    }

    [Fact]
    public void Triage_schema_matches_the_record_and_enum()
    {
        var schema = ClaimsJson.TriageSchema;
        var properties = schema["properties"].EnumerateObject().Select(p => p.Name).ToHashSet();
        var required = schema["required"].EnumerateArray().Select(e => e.GetString()!).ToHashSet();
        var recordProperties = typeof(ClaimTriage).GetProperties()
            .Select(p => JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name)).ToHashSet();
        var actions = schema["properties"].GetProperty("recommended_action").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()!).ToHashSet();

        Assert.Equal(properties, required);
        Assert.Equal(recordProperties, properties);
        Assert.Equal(Enum.GetNames<TriageAction>().Select(JsonNamingPolicy.SnakeCaseLower.ConvertName).ToHashSet(), actions);
    }
}
