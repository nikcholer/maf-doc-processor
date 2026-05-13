using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class ReceiptValidationRepairExecutor(
    IReceiptExtractor extractor,
    CancellationToken workflowCancellationToken = default)
    : Executor<ValidatedReceiptExtraction, ValidatedReceiptExtraction>("ReceiptValidationRepair")
{
    public override async ValueTask<ValidatedReceiptExtraction> HandleAsync(
        ValidatedReceiptExtraction message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (message.Validation.IsValid)
        {
            return message;
        }

        var extraction = message.Extraction;
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            workflowCancellationToken);
        var repaired = await extractor.ExtractReceiptAsync(
            extraction.ClassifiedDocument.Request,
            linkedCancellation.Token,
            message.Validation.Reasons);

        var repairedExtraction = extraction with
        {
            Receipt = repaired.Value,
            ExtractionUsages = [.. extraction.ExtractionUsages, repaired.Usage]
        };

        return new ValidatedReceiptExtraction(
            repairedExtraction,
            ReceiptValidationExecutor.Validate(repaired.Value));
    }
}
