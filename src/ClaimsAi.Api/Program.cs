using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using ClaimsAi.Core;
using ClaimsAi.Infrastructure.Claude;
using ClaimsAi.Infrastructure.Crm;
using ClaimsAi.Infrastructure.Policies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});

// Composition root: the only place that knows which Infrastructure implementations back the Core interfaces.
// API key: the ANTHROPIC_API_KEY env var, or `dotnet user-secrets set "Anthropic:ApiKey" "<key>"` in this project.
builder.Services.AddSingleton(_ =>
{
    var apiKey = builder.Configuration["Anthropic:ApiKey"];
    return string.IsNullOrWhiteSpace(apiKey) ? new AnthropicClient() : new AnthropicClient { ApiKey = apiKey };
});
builder.Services.AddSingleton<IClaimExtractor>(sp => new ClaudeClaimExtractor(sp.GetRequiredService<AnthropicClient>()));
builder.Services.AddSingleton<ICoverageChecker>(sp => new ClaudeCoverageChecker(sp.GetRequiredService<AnthropicClient>()));
builder.Services.AddSingleton<IPolicyRetriever>(_ =>
{
    var directory = Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Policies:Directory"]!);
    return new Bm25PolicyRetriever(MarkdownPolicyLoader.LoadDirectory(directory));
});
builder.Services.AddSingleton<IPolicyholderDirectory>(_ =>
    JsonPolicyholderDirectory.LoadFile(Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Crm:File"]!)));
builder.Services.AddSingleton<ClaimAgentTools>();
builder.Services.AddSingleton<IClaimTriageAgent>(sp => new ClaudeClaimAgent(
    sp.GetRequiredService<AnthropicClient>(), sp.GetRequiredService<ClaimAgentTools>()));
builder.Services.AddSingleton<ClaimAssessmentService>();

var app = builder.Build();

app.MapGet("/", () => "claims-ai-dotnet is running. POST /claims/extract or /claims/assess with { \"text\": \"...\" }");

// Step 1 only: structured data from the claim text.
app.MapPost("/claims/extract", (ClaimRequest request, IClaimExtractor extractor, CancellationToken ct) =>
    Handle(request, async submission =>
    {
        var result = await extractor.ExtractAsync(submission, ct);
        return new
        {
            result.Extraction,
            Usage = new { result.InputTokens, result.OutputTokens },
            LatencyMs = (int)result.Latency.TotalMilliseconds,
        };
    }));

// Full pipeline: extraction, retrieval of policy clauses, coverage decision with citations, triage agent.
app.MapPost("/claims/assess", (ClaimRequest request, ClaimAssessmentService assessor, CancellationToken ct) =>
    Handle(request, async submission =>
    {
        var result = await assessor.AssessAsync(submission, ct);
        return new
        {
            result.Extraction.Extraction,
            Coverage = result.Coverage.Decision,
            result.Triage.Triage,
            AgentToolCalls = result.Triage.ToolCalls,
            RetrievedClauses = result.RetrievedClauses.Select(c => new { c.Id, c.Section, c.Text }),
            Usage = new
            {
                InputTokens = result.Extraction.InputTokens + result.Coverage.InputTokens + result.Triage.InputTokens,
                OutputTokens = result.Extraction.OutputTokens + result.Coverage.OutputTokens + result.Triage.OutputTokens,
            },
            LatencyMs = (int)(result.Extraction.Latency + result.Coverage.Latency + result.Triage.Latency).TotalMilliseconds,
        };
    }));

app.Run();

static async Task<IResult> Handle<T>(ClaimRequest request, Func<ClaimSubmission, Task<T>> process)
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest(new { error = "text is required" });
    }

    try
    {
        var submission = new ClaimSubmission(request.Text, request.ReceivedAt ?? DateOnly.FromDateTime(DateTime.UtcNow));
        return Results.Ok(await process(submission));
    }
    catch (ClaimProcessingException ex)
    {
        return Results.UnprocessableEntity(new { error = ex.Message });
    }
}

internal sealed record ClaimRequest(string Text, DateOnly? ReceivedAt);
