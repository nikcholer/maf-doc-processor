using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class ReceiptExtractionExecutor(IReceiptExtractor extractor)
    : Executor<ClassifiedDocument, ReceiptExtraction>("ReceiptExtraction")
{
    public override async ValueTask<ReceiptExtraction> HandleAsync(
        ClassifiedDocument message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (message.Classification.Category != DocumentCategory.Receipt)
        {
            throw new InvalidOperationException(
                $"Receipt extraction received a {message.Classification.Category} document.");
        }

        var extraction = await extractor.ExtractReceiptAsync(message.Request, cancellationToken);
        return new ReceiptExtraction(
            message,
            extraction.Value,
            extraction.Usage);
    }
}
