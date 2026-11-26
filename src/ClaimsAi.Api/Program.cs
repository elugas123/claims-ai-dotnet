using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using ClaimsAi.Core;
using ClaimsAi.Infrastructure.Claude;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});

// API key: `dotnet user-secrets set "Anthropic:ApiKey" "<key>"` in this project, or the ANTHROPIC_API_KEY env var.
builder.Services.AddSingleton(_ =>
{
    var apiKey = builder.Configuration["Anthropic:ApiKey"];
    return string.IsNullOrWhiteSpace(apiKey) ? new AnthropicClient() : new AnthropicClient { ApiKey = apiKey };
});
builder.Services.AddSingleton<IClaimExtractor>(sp => new ClaudeClaimExtractor(sp.GetRequiredService<AnthropicClient>()));

var app = builder.Build();

app.MapGet("/", () => "claims-ai-dotnet is running. POST /claims/extract with { \"text\": \"...\" }");

app.MapPost("/claims/extract", async (ExtractRequest request, IClaimExtractor extractor, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest(new { error = "text is required" });
    }

    var submission = new ClaimSubmission(request.Text, request.ReceivedAt ?? DateOnly.FromDateTime(DateTime.UtcNow));
    try
    {
        var result = await extractor.ExtractAsync(submission, ct);
        return Results.Ok(new
        {
            result.Extraction,
            Usage = new { result.InputTokens, result.OutputTokens },
            LatencyMs = (int)result.Latency.TotalMilliseconds,
        });
    }
    catch (ClaimExtractionException ex)
    {
        return Results.UnprocessableEntity(new { error = ex.Message });
    }
});

app.Run();

internal sealed record ExtractRequest(string Text, DateOnly? ReceivedAt);
