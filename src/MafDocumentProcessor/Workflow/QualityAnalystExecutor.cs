using System.Text.Json;
using System.Text.Json.Serialization;
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class QualityAnalystExecutor(
    IModelChatClient chatClient,
    ModelRoleSettings settings,
    CancellationToken workflowCancellationToken = default)
    : Executor<DocumentProcessingResult, QualityAnalysis>("QualityAnalyst")
{
    private const string Operation = "quality_analyst";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public override async ValueTask<QualityAnalysis> HandleAsync(
        DocumentProcessingResult message,
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
                    You are the AnalystAgent for a document-processing quality review.
                    Inspect the structured workflow result and summarize quality risks, missing fields, contradictions, and confidence concerns.
                    Do not approve or reject. Return concise plain text only.
                    """),
                    ModelChatMessage.CreateUser(
                        new ModelTextContent(JsonSerializer.Serialize(message, JsonOptions)))
                ],
                MaxOutputTokens: 500),
            linkedCancellation.Token);

        if (string.IsNullOrWhiteSpace(response.Content))
        {
            throw new DocumentModelResponseException("The quality analyst model returned an empty response.");
        }

        return new QualityAnalysis(message, response.Content.Trim(), response.Usage);
    }
}
