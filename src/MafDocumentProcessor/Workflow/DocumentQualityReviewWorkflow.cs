using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;

namespace MafDocumentProcessor.Workflow;

public sealed class DocumentQualityReviewWorkflow(
    IModelChatClient chatClient,
    ModelRoleSettings settings)
{
    public async Task<QualityReviewResult> RunAsync(
        DocumentProcessingResult result,
        CancellationToken cancellationToken = default)
    {
        var analyst = new QualityAnalystExecutor(chatClient, settings, cancellationToken);
        var critic = new QualityCriticExecutor(chatClient, settings, cancellationToken);

        var analysis = await analyst.HandleAsync(result, context: null!, cancellationToken);
        return await critic.HandleAsync(analysis, context: null!, cancellationToken);
    }
}
