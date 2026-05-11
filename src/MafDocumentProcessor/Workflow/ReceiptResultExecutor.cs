using MafDocumentProcessor.Domain;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class ReceiptResultExecutor()
    : Executor<ReceiptPolicyEvaluation, ReceiptProcessingResult>("ReceiptResult")
{
    public override ValueTask<ReceiptProcessingResult> HandleAsync(
        ReceiptPolicyEvaluation message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var extraction = message.ValidatedExtraction.Extraction;
        var classifiedDocument = extraction.ClassifiedDocument;
        var modelUsage = DocumentModelUsage.FromCalls(
            [
                classifiedDocument.ClassificationUsage,
                extraction.ExtractionUsage
            ]);

        var warnings = message.Validation.IsValid ? [] : message.Validation.Reasons;

        return ValueTask.FromResult(new ReceiptProcessingResult(
            classifiedDocument.Classification.Category,
            classifiedDocument.Metadata,
            classifiedDocument.Classification,
            modelUsage,
            extraction.Receipt,
            message.PolicyResult,
            message.Validation,
            IsSuccess: true,
            Errors: [],
            Warnings: warnings));
    }
}
