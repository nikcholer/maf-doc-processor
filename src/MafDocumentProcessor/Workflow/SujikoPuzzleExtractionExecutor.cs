using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class SujikoPuzzleExtractionExecutor(
    ISujikoPuzzleExtractor extractor,
    CancellationToken workflowCancellationToken = default)
    : Executor<ClassifiedDocument, SujikoPuzzleExtraction>("SujikoPuzzleExtraction")
{
    public override async ValueTask<SujikoPuzzleExtraction> HandleAsync(
        ClassifiedDocument message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (message.Classification.Category != DocumentCategory.SujikoPuzzle)
        {
            throw new InvalidOperationException(
                $"Sujiko puzzle extraction received a {message.Classification.Category} document.");
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            workflowCancellationToken);
        var extraction = await extractor.ExtractSujikoPuzzleAsync(message.Request, linkedCancellation.Token);
        return new SujikoPuzzleExtraction(
            message,
            extraction.Value,
            extraction.Usage);
    }
}
