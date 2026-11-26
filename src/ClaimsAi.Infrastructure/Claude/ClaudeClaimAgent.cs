using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using ClaimsAi.Core;

namespace ClaimsAi.Infrastructure.Claude;

/// <summary>
/// The triage agent: Claude decides which lookups to make, we run them, and the loop repeats until it returns
/// its triage as structured output. The loop runs here, in our service, so the data never leaves our tools
/// and every call is visible in <see cref="TriageResult.ToolCalls"/>.
/// </summary>
public sealed class ClaudeClaimAgent(AnthropicClient client, ClaimAgentTools tools, ClaudeOptions? options = null)
    : IClaimTriageAgent
{
    /// <summary>Safety stop: a claim needs 2-4 lookups, so 10 turns means the agent is stuck.</summary>
    public const int MaxTurns = 10;

    private readonly ClaudeOptions _options = options ?? new() { MaxTokens = 8000 };

    internal const string SystemPrompt = """
        You are the claims triage agent of Contoso Insurance, a fictional insurer. You receive a claim: the customer's
        report, the data extracted from it, and a coverage decision made from the policy clauses. Investigate with the
        tools, then return your triage.

        How to investigate:
        - When there is a policy number, look up the policyholder and the claim history.
        - Use search_policy for clauses you still need, especially the reporting obligations (deadlines, required
          documents) for this type of claim.
        - Compare what the customer says with the records: incident date vs. policy dates and status, plate or address
          vs. the ones on file.

        Fraud signals (flag only what the evidence supports, and quote it):
        - The incident happened within 30 days of the policy start date.
        - Two or more claims in the 12 months before the incident.
        - The vehicle plate or address doesn't match the policy records.
        A policy that wasn't active on the incident date is a coverage problem, not a fraud signal.

        recommended_action, in this priority order (use the first that applies):
        1. deny: the coverage decision is not_covered, or the policy wasn't active on the incident date.
        2. refer_to_fraud_team: at least one fraud signal.
        3. request_information: a fact or document the policy requires is missing (policy number, date, police
           report, plumber's report, repair estimate, ...).
        4. manual_review: covered, but injuries, third-party liability, or an amount over USD 10,000 need an adjuster.
        5. fast_track: covered, complete, and no signals.

        customer_reply: a short, polite message in the same language the customer wrote in. Say what happens next and
        exactly what they must send, if anything. Never mention fraud checks and never promise an amount.
        internal_notes: two or three sentences in English for the adjuster, with the evidence behind the action.
        """;

    public async Task<TriageResult> TriageAsync(
        ClaimSubmission submission,
        ClaimExtraction claim,
        CoverageDecision coverage,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        List<MessageParam> messages = [new UserMessageParam { Content = BuildUserMessage(submission, claim, coverage) }];
        List<AgentToolCall> toolCalls = [];
        long inputTokens = 0, outputTokens = 0;

        for (var turn = 1; turn <= MaxTurns; turn++)
        {
            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model = _options.Model,
                MaxTokens = _options.MaxTokens,
                System = SystemPrompt,
                Tools = [.. ClaimAgentTools.Definitions],
                OutputConfig = new OutputConfig
                {
                    Effort = _options.Effort,
                    Format = new JsonOutputFormat { Schema = ClaimsJson.TriageSchema },
                },
                Messages = [.. messages],
            }, cancellationToken);

            inputTokens += response.Usage.InputTokens;
            outputTokens += response.Usage.OutputTokens;

            if (response.StopReason == "refusal")
            {
                throw new ClaimProcessingException($"The model declined this request: {response.StopDetails?.Explanation}");
            }
            if (response.StopReason == "max_tokens")
            {
                throw new ClaimProcessingException($"The agent's answer was cut off at {_options.MaxTokens} tokens.");
            }
            if (response.StopReason != "tool_use")
            {
                var json = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
                var triage = ClaimsJson.Deserialize<ClaimTriage>(json);
                return new TriageResult(triage, toolCalls, turn, inputTokens, outputTokens, stopwatch.Elapsed);
            }

            // Echo the assistant turn back unchanged (thinking blocks keep their signature), then answer every tool call
            // in a single user message.
            List<ContentBlockParam> assistantContent = [];
            List<ContentBlockParam> toolResults = [];
            foreach (var block in response.Content)
            {
                if (block.TryPickText(out var text))
                {
                    assistantContent.Add(new TextBlockParam { Text = text.Text });
                }
                else if (block.TryPickThinking(out var thinking))
                {
                    assistantContent.Add(new ThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                }
                else if (block.TryPickRedactedThinking(out var redacted))
                {
                    assistantContent.Add(new RedactedThinkingBlockParam { Data = redacted.Data });
                }
                else if (block.TryPickToolUse(out var toolUse))
                {
                    assistantContent.Add(new ToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });

                    var (content, isError) = tools.Execute(toolUse.Name, toolUse.Input);
                    toolCalls.Add(new AgentToolCall(toolUse.Name, JsonSerializer.Serialize(toolUse.Input), isError));
                    toolResults.Add(new ToolResultBlockParam { ToolUseID = toolUse.ID, Content = content, IsError = isError });
                }
            }

            messages.Add(new AssistantMessageParam { Content = assistantContent });
            messages.Add(new UserMessageParam { Content = toolResults });
        }

        throw new ClaimProcessingException($"The agent didn't finish within {MaxTurns} turns.");
    }

    internal static string BuildUserMessage(ClaimSubmission submission, ClaimExtraction claim, CoverageDecision coverage)
    {
        var text = new StringBuilder();
        text.AppendLine($"Received date: {submission.ReceivedAt:yyyy-MM-dd}");
        text.AppendLine();
        text.AppendLine("<claim_report>");
        text.AppendLine(submission.Text);
        text.AppendLine("</claim_report>");
        text.AppendLine();
        text.AppendLine("<claim_data>");
        text.AppendLine(ClaimsJson.Serialize(claim));
        text.AppendLine("</claim_data>");
        text.AppendLine();
        text.AppendLine("<coverage_decision>");
        text.AppendLine(ClaimsJson.Serialize(coverage));
        text.AppendLine("</coverage_decision>");
        return text.ToString();
    }
}
