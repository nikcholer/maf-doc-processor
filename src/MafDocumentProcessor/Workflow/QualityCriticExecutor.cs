using System.Text.Json;
using System.Text.Json.Serialization;
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class QualityCriticExecutor(
    IModelChatClient chatClient,
    ModelRoleSettings settings,
    CancellationToken workflowCancellationToken = default)
    : Executor<QualityAnalysis, QualityReviewResult>("QualityCritic")
{
    private const string Operation = "quality_critic";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public override async ValueTask<QualityReviewResult> HandleAsync(
        QualityAnalysis message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            workflowCancellationToken);
        var response = await chatClient.CompleteAsync(
            new ModelChatRequest(
                Operation,
                settings,
                [
                    ModelChatMessage.CreateSystem("""
                    You are the CriticAgent for a document-processing quality review.
                    Use the analyst summary and structured workflow result to decide whether the result is acceptable.
                    Return only compact JSON with this exact shape:
                    {"decision":"Accept|NeedsHumanReview|Reject","findings":[{"severity":"Info|Warning|Error","message":"string"}]}
                    """),
                    ModelChatMessage.CreateUser(
                        new ModelTextContent(JsonSerializer.Serialize(new
                        {
                            message.AnalystSummary,
                            message.DocumentResult
                        }, JsonOptions)))
                ],
                MaxOutputTokens: 500),
            linkedCancellation.Token);

        var result = ParseCriticResponse(response.Content);
        return result with
        {
            ModelUsage = DocumentModelUsage.FromCalls([message.AnalystUsage, response.Usage])
        };
    }

    private static QualityReviewResult ParseCriticResponse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DocumentModelResponseException("The quality critic model returned an empty response.");
        }

        using var document = ParseJson(content);
        var root = document.RootElement;
        var decisionText = root.GetProperty("decision").GetString();
        if (!Enum.TryParse<QualityReviewDecision>(decisionText, ignoreCase: true, out var decision))
        {
            throw new DocumentModelResponseException(
                $"The quality critic model returned unsupported decision '{decisionText}'.");
        }

        var findings = new List<QualityReviewFinding>();
        if (root.TryGetProperty("findings", out var findingsElement)
            && findingsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var finding in findingsElement.EnumerateArray())
            {
                var severityText = finding.GetProperty("severity").GetString();
                if (!Enum.TryParse<QualityReviewFindingSeverity>(severityText, ignoreCase: true, out var severity))
                {
                    throw new DocumentModelResponseException(
                        $"The quality critic model returned unsupported finding severity '{severityText}'.");
                }

                findings.Add(new QualityReviewFinding(
                    severity,
                    finding.GetProperty("message").GetString() ?? string.Empty));
            }
        }

        return new QualityReviewResult(
            decision,
            findings,
            DocumentModelUsage.FromCalls([]));
    }

    private static JsonDocument ParseJson(string content)
    {
        try
        {
            return JsonDocument.Parse(NormalizeJsonObject(content));
        }
        catch (JsonException ex)
        {
            throw new DocumentModelResponseException(
                "The quality critic model returned invalid JSON.",
                ex);
        }
    }

    private static string NormalizeJsonObject(string content)
    {
        var value = content.Trim();
        if (value.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineBreak = value.IndexOf('\n');
            if (firstLineBreak >= 0)
            {
                value = value[(firstLineBreak + 1)..];
            }

            var lastFence = value.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
            {
                value = value[..lastFence];
            }
        }

        var start = value.IndexOf('{');
        var end = value.LastIndexOf('}');
        return start >= 0 && end > start
            ? value[start..(end + 1)]
            : value;
    }
}
